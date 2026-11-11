using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal sealed class BodyCamBreath : MonoBehaviour
    {
        internal static ConfigEntry<float> CfgVolume;

        internal static void BindConfig()
        {
            CfgVolume = ConfigMigration.Bind(
                Plugin.SecSounds, "Breathing Volume", 0.03f,
                "Breathing by stamina, wounds and bullets flying past (0 = off, Tarkov breathes).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 95, null, null);
        }

        private static readonly string[] Files =
        {
            "Breath_Calm.wav",
            "Breath_Run.wav",
            "Breath_Stop_1.wav",
            "Breath_Stop_2.wav"
        };

        private const int Calm = 0, Run = 1, Stop1 = 2, Stop2 = 3;

        private const float StressRest = 0.1f;
        private const float StressPerBullet = 0.3f;
        private const float StressDecay = 0.1f;
        private const float RunFade = 0.3f;
        private const float HoldFade = 0.3f;
        private const float OnFade = 0.5f;

        private const float HeavyScale = 1f / 1.2f;
        private const float CalmScale = 1.37f;
        private const float PeakTarget = 0.89f;

        private static BodyCamBreath _instance;

        internal static bool Active;

        private readonly AudioClip[] _clips = new AudioClip[4];
        private readonly AudioSource[] _src = new AudioSource[4];
        private int _loadNext;
        private bool _missing;
        private GameObject _host;

        private float _stress = StressRest;
        private float _speed;
        private float _run;
        private float _hold = 1f;
        private float _on;
        private int _stop = Stop1;
        private bool _wasRunning;
        private bool _wasActive;
        private Player _player;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _moveSpeed;
        private float _nextLog;

        internal static void Create()
        {
            if (_instance != null)
                return;

            var go = new GameObject("BodyCamera_Breath");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<BodyCamBreath>();
        }

        internal static void ResetAudio()
        {
            if (_instance == null)
                return;

            _instance.DestroySources();

            _instance.DropClips();

            _instance._stress = StressRest;
            _instance._hasLastPos = false;
            _instance._on = 0f;
            Active = false;
            _instance._wasActive = false;
        }

        internal static void OnFlyBy()
        {
            if (_instance != null && Active)
                _instance._stress = Mathf.Min(2f, _instance._stress + StressPerBullet);
        }

        private void Update()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            Player player = null;

            try
            {
                player = Singleton<GameWorld>.Instance?.MainPlayer;
            }
            catch
            {
            }

            if (!ReferenceEquals(player, _player))
            {
                _player = player;
                _hasLastPos = false;
            }

            bool alive = false;

            try
            {
                alive = player != null && player.HealthController != null && player.HealthController.IsAlive;
            }
            catch
            {
            }

            float volume = CfgVolume != null ? Mathf.Max(0f, CfgVolume.Value) : 0f;

            bool started = false;

            try
            {
                started = Plugin.StartedWorld != null &&
                          ReferenceEquals(Plugin.StartedWorld, Singleton<GameWorld>.Instance);
            }
            catch
            {
            }

            bool want = alive && started && Plugin.Enabled != null && Plugin.Enabled.Value && volume > 0.001f;

            if (want && !LoadClips())
                want = false;

            Active = want;

            if (_wasActive && !want)
                GiveBackToTarkov();

            _wasActive = want;

            _on = Mathf.MoveTowards(_on, want ? 1f : 0f, dt / OnFade);

            if (!want && _on <= 0f)
            {
                if (_host != null)
                    SetVolumes(0f, 0f, 0f, 0f);

                return;
            }

            if (_host == null && !BuildSources())
                return;

            float fatigue = 0f, wounds = 0f;
            bool sprint = false, holding = false;

            if (player != null)
            {
                try
                {
                    Stamina st = player.Physical.Stamina;
                    float max = st.TotalCapacity.Value;

                    if (max > 0.01f)
                        fatigue = 1f - Mathf.Clamp01(st.Current / max);

                    holding = player.Physical.HoldingBreath;
                }
                catch
                {
                }

                try
                {
                    var hp = player.HealthController.GetBodyPartHealth(EBodyPart.Common, false);

                    if (hp.Maximum > 0.01f)
                        wounds = Map(100f * hp.Current / hp.Maximum, 25f, 100f, 0.8f, 0f);
                }
                catch
                {
                }

                Vector3 pos = player.GetComponent<Transform>().position;

                if (_hasLastPos && dt > 0f)
                {
                    Vector3 d = pos - _lastPos;
                    d.y = 0f;
                    _moveSpeed = Mathf.Lerp(_moveSpeed, Mathf.Min(d.magnitude / dt, 10f), 1f - Mathf.Exp(-8f * dt));
                }

                _lastPos = pos;
                _hasLastPos = true;

                sprint = SprintState.IsSprinting(player) && _moveSpeed > 1.5f;
            }

            _stress = Mathf.Max(StressRest, _stress - StressDecay * dt);

            float target = Mathf.Clamp01(_stress + wounds + fatigue);
            _speed = Mathf.MoveTowards(_speed, target, dt * 2f);

            if (_wasRunning && !sprint)
                _stop = UnityEngine.Random.value < 0.5f || _clips[Stop2] == null ? Stop1 : Stop2;

            _wasRunning = sprint;
            _run = Mathf.MoveTowards(_run, sprint ? 1f : 0f, dt / RunFade);
            _hold = Mathf.MoveTowards(_hold, holding ? 0f : 1f, dt / HoldFade);

            float calmW = CrossFade(_speed, 0f, 0f, 0.4f, 0.5f) * 1.0f;
            float heavyW = CrossFade(_speed, 0.6f, 1.0f, 1.1f, 1.1f) * 1.5f;
            float mul = Map(_speed, 0.4f, 1f, 0.3f, 0.8f);
            float master = volume * _hold * Mathf.SmoothStep(0f, 1f, _on) * mul;
            float calm = calmW * master * CalmScale;
            float heavy = heavyW * master * HeavyScale;

            float stop1 = _stop == Stop1 ? 1f : 0f;
            SetVolumes(
                calm,
                heavy * _run,
                heavy * (1f - _run) * stop1,
                heavy * (1f - _run) * (1f - stop1));

            PlaceAtHead(dt);

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value && Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo(
                    "[BodyCamera] Breath: speed " + _speed.ToString("0.00") +
                    " (fatigue " + fatigue.ToString("0.00") + ", wounds " + wounds.ToString("0.00") +
                    ", stress " + _stress.ToString("0.00") + "), sprint " + sprint +
                    ", loops " + Describe() + ".");
            }
        }

        private float _space;
        private AudioListener _listener;
        private float _nextListener;

        private void PlaceAtHead(float dt)
        {
            if (_host == null)
                return;

            bool gun = BodyCamGunCam.RecentlyActive;
            float want = Mathf.Max(Mathf.Clamp01(Plugin.ThirdWeight), gun ? 1f : 0f);

            _space = gun ? want : Mathf.MoveTowards(_space, want, dt / 0.35f);

            if (_space <= 0.001f)
            {
                SetSpace(0f);
                return;
            }

            Camera cam = Camera.main;

            if (cam == null || Plugin.HeadPosition == Vector3.zero)
            {
                SetSpace(0f);
                return;
            }

            if ((_listener == null || !_listener.isActiveAndEnabled) && Time.unscaledTime >= _nextListener)
            {
                _nextListener = Time.unscaledTime + 1f;
                _listener = FindObjectOfType<AudioListener>();
            }

            Transform ears = _listener != null && _listener.isActiveAndEnabled ? _listener.transform : cam.transform;

            Vector3 viewPos = gun ? BodyCamGunCam.LastViewPosition : cam.transform.position;
            Quaternion viewRot = gun ? BodyCamGunCam.LastViewRotation : cam.transform.rotation;

            Vector3 local = Quaternion.Inverse(viewRot) * (Plugin.HeadPosition - viewPos);

            if (local.sqrMagnitude > 100f)
            {
                SetSpace(0f);
                return;
            }

            _host.transform.position = ears.position + ears.rotation * local;
            SetSpace(_space);
        }

        private void SetSpace(float blend)
        {
            for (int i = 0; i < _src.Length; i++)
            {
                if (_src[i] != null)
                    _src[i].spatialBlend = blend;
            }
        }

        private void GiveBackToTarkov()
        {
            try
            {
                if (_player != null)
                {
                    _player.HeavyBreath = false;
                    _player.UpdateBreathStatus();
                }
            }
            catch
            {
            }
        }

        private bool LoadClips()
        {
            if (_missing)
                return false;

            if (_loadNext >= Files.Length)
                return true;

            string path = Path.Combine(Path.Combine(BodyCamSounds.SoundRoot(), "breath"), Files[_loadNext]);

            if (!File.Exists(path))
            {
                _missing = true;
                Plugin.Log.LogWarning("[BodyCamera] Breathing sound missing: " + path + " - Tarkov's breathing is kept.");
                return false;
            }

            _clips[_loadNext] = BodyCamSounds.LoadWav(path);

            if (_clips[_loadNext] == null)
            {
                _missing = true;
                return false;
            }

            Normalize(_clips[_loadNext]);

            _loadNext++;

            if (_loadNext >= Files.Length)
                Plugin.Log.LogInfo("[BodyCamera] Breathing sounds loaded.");

            return _loadNext >= Files.Length;
        }

        private static void Normalize(AudioClip clip)
        {
            try
            {
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                float peak = 0f;

                for (int i = 0; i < data.Length; i++)
                {
                    float a = Mathf.Abs(data[i]);

                    if (a > peak)
                        peak = a;
                }

                if (peak < 0.0001f)
                    return;

                float g = PeakTarget / peak;

                for (int i = 0; i < data.Length; i++)
                    data[i] *= g;

                clip.SetData(data, 0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Breathing sound level: " + ex.Message);
            }
        }

        private bool BuildSources()
        {
            if (_loadNext < Files.Length)
                return false;

            _host = new GameObject("BodyCamera_BreathSources");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;

            for (int i = 0; i < _src.Length; i++)
            {
                var s = _host.AddComponent<AudioSource>();
                s.clip = _clips[i];
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;

                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 0.6f;
                s.maxDistance = 25f;
                s.spread = 0f;
                s.priority = 0;
                s.volume = 0f;
                s.bypassReverbZones = true;
                s.dopplerLevel = 0f;
                s.timeSamples = UnityEngine.Random.Range(0, Mathf.Max(1, _clips[i].samples - 1));
                s.Play();
                _src[i] = s;
            }

            return true;
        }

        private void DropClips()
        {
            for (int i = 0; i < _clips.Length; i++)
            {
                try
                {
                    if (_clips[i] != null)
                        Destroy(_clips[i]);
                }
                catch
                {
                }

                _clips[i] = null;
            }

            _loadNext = 0;
            _missing = false;
        }

        private void DestroySources()
        {
            if (_host != null)
                Destroy(_host);

            _host = null;

            for (int i = 0; i < _src.Length; i++)
                _src[i] = null;
        }

        private void SetVolumes(float calm, float run, float stop1, float stop2)
        {
            Set(Calm, calm);
            Set(Run, run);
            Set(Stop1, stop1);
            Set(Stop2, stop2);
        }

        private void Set(int i, float v)
        {
            AudioSource s = _src[i];

            if (s == null)
                return;

            s.volume = Mathf.Clamp01(v);

            if (!s.isPlaying && s.clip != null)
                s.Play();
        }

        private string Describe()
        {
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < _src.Length; i++)
            {
                AudioSource s = _src[i];

                if (i > 0)
                    sb.Append(' ');

                if (s == null)
                    sb.Append("none");
                else
                    sb.Append(s.volume.ToString("0.00")).Append(s.isPlaying ? "" : "(stopped)");
            }

            sb.Append(", listener ").Append(AudioListener.volume.ToString("0.00"))
              .Append(AudioListener.pause ? " paused" : "");
            return sb.ToString();
        }

        private static float Map(float v, float inA, float inB, float outA, float outB)
        {
            return Mathf.Lerp(outA, outB, Mathf.Clamp01((v - inA) / (inB - inA)));
        }

        private static float CrossFade(float p, float a, float b, float c, float d)
        {
            float fadeIn = b <= a ? (p >= a ? 1f : 0f) : Mathf.Clamp01((p - a) / (b - a));
            float fadeOut = d <= c ? (p <= c ? 1f : 0f) : 1f - Mathf.Clamp01((p - c) / (d - c));
            return fadeIn * fadeOut;
        }
    }

    internal sealed class BodyCamBreathSpeakerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BaseSpeaker).GetMethod(
                "Play",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(bool), typeof(int?) },
                null);
        }

        [PatchPrefix]
        private static bool Prefix(BaseSpeaker __instance, EPhraseTrigger trigger, ref TagBank __result)
        {
            if (trigger != EPhraseTrigger.OnBreath || !BodyCamBreath.Active)
                return true;

            try
            {
                var main = Singleton<GameWorld>.Instance?.MainPlayer;

                if (main == null || !ReferenceEquals(main.Speaker, __instance))
                    return true;
            }
            catch
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

    internal sealed class BodyCamFlyByPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(FlyingBulletSoundPlayer).GetMethod(
                "PlaySound",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix()
        {
            BodyCamBreath.OnFlyBy();
        }
    }
}

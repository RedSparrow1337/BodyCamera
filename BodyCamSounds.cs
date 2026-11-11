using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal sealed class BodyCamSounds : MonoBehaviour
    {
        internal static ConfigEntry<bool> CfgWind;
        internal static ConfigEntry<float> CfgWindVolume;
        internal static ConfigEntry<bool> CfgTurn;
        internal static ConfigEntry<float> CfgTurnVolume;
        internal static ConfigEntry<bool> CfgStep;
        internal static ConfigEntry<float> CfgStepVolume;
        internal static ConfigEntry<bool> CfgTremor;
        internal static ConfigEntry<float> CfgTremorVolume;

        internal static void BindConfig()
        {
            CfgWind = ConfigMigration.Bind(
                Plugin.SecSounds, "Wind", true,
                "Wind noise on the camera microphone.",
                null, 100, "8. Wind", "Enabled");

            CfgWindVolume = ConfigMigration.Bind(
                Plugin.SecSounds, "Wind Volume", 0.20f,
                "Wind volume.",
                new AcceptableValueRange<float>(0f, 2f), 95, null, null);

            CfgTurn = ConfigMigration.Bind(
                Plugin.SecSounds, "Turn Sounds", true,
                "Clothes and gear rubbing when you turn.",
                null, 90, null, null);

            CfgTurnVolume = ConfigMigration.Bind(
                Plugin.SecSounds, "Turn Sounds Volume", 1.00f,
                "Turn sounds volume.",
                new AcceptableValueRange<float>(0f, 2f), 85, null, null);

            CfgStep = ConfigMigration.Bind(
                Plugin.SecSounds, "Step Sounds", true,
                "Camera and mount knocks on every step.",
                null, 80, null, null);

            CfgStepVolume = ConfigMigration.Bind(
                Plugin.SecSounds, "Step Sounds Volume", 0.08f,
                "Step sounds volume.",
                new AcceptableValueRange<float>(0f, 3f), 75, null, null);

            CfgTremor = ConfigMigration.Bind(
                Plugin.SecSounds, "Mic Tremor", true,
                "Microphone rattle when you fire.",
                null, 70, null, null);

            CfgTremorVolume = ConfigMigration.Bind(
                Plugin.SecSounds, "Mic Tremor Volume", 1.00f,
                "Mic tremor volume.",
                new AcceptableValueRange<float>(0f, 2f), 65, null, null);
        }

        private const float DegPerRateUnit = 125f;
        private const float TremorPerShot = 0.04f;

        private sealed class StepLayer
        {
            internal string Set;
            internal int Group;
            internal int With = -1;
            internal float Chance;
            internal float Volume;
            internal float PitchMin = 1f, PitchMax = 1f;
            internal float DelayMin, DelayMax;
        }

        private sealed class StepCue
        {
            internal float GroupA;
            internal float GroupB;
            internal StepLayer[] Layers;
        }

        private static StepLayer L(string set, float chance, float volume, int group = 0,
                                   float pMin = 1f, float pMax = 1f, float dMin = 0f, float dMax = 0f, int with = -1)
        {
            return new StepLayer
            {
                Set = set, Chance = chance, Volume = volume, Group = group,
                PitchMin = pMin, PitchMax = pMax, DelayMin = dMin, DelayMax = dMax, With = with
            };
        }

        private static readonly StepCue Walk = new StepCue
        {
            GroupA = 1f / 3f,
            GroupB = 1f / 15f,
            Layers = new[]
            {
                L("Friction_Walk", 0.12f, 0.004f),
                L("Cam_Hit", 0.33f, 0.04f, 0, 0.7f, 0.9f),
                L("Cam_Shock", 0.37f, 0.08f),
                L("Mic_Walk", 0.53f, 0.20f),
                L("Cam_Body", 0.35f, 0.12f, 1, 1f, 1f, 0f, 0.5f),
                L("Mount", 0.375f, 0.12f, 1),
                L("Mount_Metal", 0.41f, 0.12f, 1),
                L("Mount_Plastic", 0.43f, 0.12f, 1),
                L("Mic_Hit", 0.48f, 0.20f, 2),
                L("Mic_Elec", 1f, 0.04f, 2, 1f, 1f, 0.1f, 0.1f, 8),
            }
        };

        private static readonly StepCue Run = new StepCue
        {
            GroupA = 1f / 4f,
            GroupB = 1f / 15f,
            Layers = new[]
            {
                L("Friction_Run", 0.11f, 0.05f),
                L("Cam_Shock", 0.33f, 0.50f),
                L("Cam_Hit", 0.37f, 0.20f),
                L("Mic_Run", 0.72f, 0.50f),
                L("Cam_Body", 0.375f, 0.50f, 1, 1f, 1f, 0f, 0.2f),
                L("Mount", 0.43f, 0.50f, 1),
                L("Mount_Metal", 0.47f, 0.50f, 1),
                L("Mount_Plastic", 0.50f, 0.50f, 1),
                L("Mic_Hit", 0.48f, 1.00f, 2),
            }
        };

        private static readonly StepCue Crouch = new StepCue
        {
            GroupA = 1f / 3f,
            GroupB = 1f / 15f,
            Layers = new[]
            {
                L("Friction_Walk", 0.125f, 0.002f),
                L("Cam_Hit", 0.33f, 0.05f, 0, 0.7f, 0.9f),
                L("Cam_Shock", 0.37f, 0.02f),
                L("Mic_Walk", 1f, 0.05f),
                L("Cam_Body", 0.64f, 0.03f, 1, 1f, 1f, 0.2f, 0.7f),
                L("Mount", 0.69f, 0.03f, 1),
                L("Mount_Metal", 0.47f, 0.03f, 1),
                L("Mount_Plastic", 0.50f, 0.03f, 1),
                L("Mic_Hit", 0.58f, 0.05f, 2),
                L("Mic_Elec", 1f, 0.01f, 2, 1f, 1f, 0.2f, 0.2f, 8),
            }
        };

        private static BodyCamSounds _instance;

        internal static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("BodyCamera_Sounds");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<BodyCamSounds>();
        }

        internal static void ResetAudio()
        {
            if (_instance != null)
                _instance.Rebuild();
        }

        internal static void OnFootstep(Player player)
        {
            if (_instance != null)
                _instance.Footstep(player);
        }

        internal static void OnShot()
        {
            if (_instance == null || !_instance._on)
                return;

            if (CfgTremor == null || !CfgTremor.Value)
                return;

            _instance._tremor = Mathf.Clamp01(_instance._tremor + TremorPerShot);
        }

        private readonly Dictionary<string, List<AudioClip>> _sets =
            new Dictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);

        private readonly Queue<KeyValuePair<string, string>> _loadQueue =
            new Queue<KeyValuePair<string, string>>();

        private static readonly Regex NumberedFile =
            new Regex(@"^(.+)_(\d+)\.wav$", RegexOptions.IgnoreCase);

        private bool _scanned;

        internal static string SoundRoot()
        {
            string dll = typeof(BodyCamSounds).Assembly.Location;
            return Path.Combine(Path.GetDirectoryName(dll) ?? ".", "sounds");
        }

        private void Scan()
        {
            _scanned = true;
            _sets.Clear();
            _loadQueue.Clear();

            string root = SoundRoot();
            int files = 0;

            foreach (string sub in new[] { "wind", "turn", "step" })
            {
                string dir = Path.Combine(root, sub);

                if (!Directory.Exists(dir))
                {
                    Plugin.Log.LogWarning("[BodyCamera] Sound folder missing: " + dir);
                    continue;
                }

                string[] all = Directory.GetFiles(dir, "*.wav");
                Array.Sort(all, StringComparer.OrdinalIgnoreCase);

                foreach (string path in all)
                {
                    string name = Path.GetFileName(path);
                    Match m = NumberedFile.Match(name);

                    string set = m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(name);

                    if (!_sets.ContainsKey(set))
                        _sets[set] = new List<AudioClip>();

                    _loadQueue.Enqueue(new KeyValuePair<string, string>(set, path));
                    files++;
                }
            }

            Plugin.Log.LogInfo("[BodyCamera] Sounds: " + files + " files in " + _sets.Count + " sets (" + root + ").");
        }

        private void LoadSome()
        {
            int budget = 4;

            while (budget-- > 0 && _loadQueue.Count > 0)
            {
                var job = _loadQueue.Dequeue();
                AudioClip clip = LoadWav(job.Value);

                if (clip != null)
                    _sets[job.Key].Add(clip);
            }
        }

        private List<AudioClip> Set(string name)
        {
            List<AudioClip> list;
            return _sets.TryGetValue(name, out list) ? list : null;
        }

        private AudioClip One(string name)
        {
            var list = Set(name);
            return list != null && list.Count > 0 ? list[0] : null;
        }

        private static AudioClip Pick(List<AudioClip> list, ref int last)
        {
            if (list == null || list.Count == 0)
                return null;

            int i = UnityEngine.Random.Range(0, list.Count);

            if (list.Count > 1 && i == last)
                i = (i + 1 + UnityEngine.Random.Range(0, list.Count - 1)) % list.Count;

            last = i;
            return list[i];
        }

        internal static AudioClip LoadWav(string path)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);

                if (data.Length < 44 ||
                    System.Text.Encoding.ASCII.GetString(data, 0, 4) != "RIFF" ||
                    System.Text.Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
                {
                    Plugin.Log.LogWarning("[BodyCamera] Not a WAV file: " + path);
                    return null;
                }

                int channels = 1, rate = 44100, bits = 16, format = 1;
                int pos = 12;

                while (pos + 8 <= data.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    int size = BitConverter.ToInt32(data, pos + 4);
                    int body = pos + 8;

                    if (size < 0)
                        break;

                    if (id == "fmt ")
                    {
                        format = BitConverter.ToInt16(data, body);
                        channels = BitConverter.ToInt16(data, body + 2);
                        rate = BitConverter.ToInt32(data, body + 4);
                        bits = BitConverter.ToInt16(data, body + 14);
                    }
                    else if (id == "data")
                    {
                        if (bits != 16 || (format != 1 && format != -2) || channels < 1 || channels > 2)
                        {
                            Plugin.Log.LogWarning("[BodyCamera] Sound must be 16-bit PCM mono/stereo: " + path);
                            return null;
                        }

                        size = Math.Min(size, data.Length - body);
                        int count = size / 2;
                        count -= count % channels;

                        if (count <= 0)
                            return null;

                        var samples = new float[count];
                        const float k = 1f / 32768f;

                        for (int i = 0, b = body; i < count; i++, b += 2)
                            samples[i] = (short)(data[b] | (data[b + 1] << 8)) * k;

                        var clip = AudioClip.Create(
                            "bc_" + Path.GetFileNameWithoutExtension(path),
                            count / channels, channels, rate, false);
                        clip.SetData(samples, 0);
                        return clip;
                    }

                    pos = body + size + (size & 1);
                }

                Plugin.Log.LogWarning("[BodyCamera] WAV without audio data: " + path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Sound load error (" + path + "): " + ex.Message);
            }

            return null;
        }

        private GameObject _host;

        private AudioSource NewSource(bool loop)
        {
            if (_host == null)
            {
                _host = new GameObject("BodyCamera_SoundSources");
                DontDestroyOnLoad(_host);
                _host.hideFlags = HideFlags.HideAndDontSave;
            }

            var s = _host.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.priority = 0;
            s.volume = 0f;
            s.bypassReverbZones = true;
            s.dopplerLevel = 0f;
            return s;
        }

        private sealed class LoopLayer
        {
            internal string Set;
            internal AudioSource Src;

            internal void Tick(BodyCamSounds owner, float volume, float pan)
            {
                if (Src == null)
                    return;

                if (Src.clip == null)
                {
                    AudioClip clip = owner.One(Set);

                    if (clip == null)
                        return;

                    Src.clip = clip;
                    Src.loop = true;
                    Src.volume = 0f;
                    Src.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
                    Src.Play();
                }

                if (!Src.isPlaying)
                    Src.Play();

                Src.volume = Mathf.Clamp01(volume);
                Src.panStereo = pan;
            }
        }

        private sealed class SequenceLayer
        {
            internal string Set;
            internal float Delay;
            internal AudioSource Src;
            private int _last = -1;
            private float _nextAt = -1f;

            internal void Tick(BodyCamSounds owner, float volume, float pan, float now)
            {
                if (Src == null)
                    return;

                Src.volume = Mathf.Clamp01(volume);
                Src.panStereo = pan;

                if (volume <= 0.002f)
                {
                    _nextAt = -1f;
                    return;
                }

                if (Src.isPlaying)
                    return;

                if (_nextAt < 0f)
                    _nextAt = now + Delay;

                if (now < _nextAt)
                    return;

                AudioClip clip = Pick(owner.Set(Set), ref _last);

                if (clip == null)
                    return;

                Src.clip = clip;
                Src.Play();
                _nextAt = -1f;
            }
        }

        private LoopLayer _windMic, _windRun, _windShake;
        private SequenceLayer _windGust;

        private LoopLayer _clothLow, _clothHigh, _clothFast;
        private SequenceLayer _whooshCloth, _whooshClothStrong, _whooshGear, _whooshGearStrong;

        private LoopLayer _tremorShake, _tremorWind;

        private readonly List<AudioSource> _oneShots = new List<AudioSource>();
        private int _oneShotNext;

        private struct Pending
        {
            internal float At;
            internal AudioClip Clip;
            internal float Volume;
            internal float Pitch;
        }

        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Dictionary<string, int> _lastPick = new Dictionary<string, int>();

        private LoopLayer Loop(string set)
        {
            return new LoopLayer { Set = set, Src = NewSource(true) };
        }

        private SequenceLayer Sequence(string set, float delay)
        {
            return new SequenceLayer { Set = set, Delay = delay, Src = NewSource(false) };
        }

        private void BuildSources()
        {
            _windMic = Loop("Wind_Mic_Loop");
            _windRun = Loop("Wind_Run_Loop");
            _windShake = Loop("Wind_Shake_Loop");
            _windGust = Sequence("Wind_Gust", 0f);

            _clothLow = Loop("Cloth_Low_Loop");
            _clothHigh = Loop("Cloth_High_Loop");
            _clothFast = Loop("Cloth_Fast_Loop");
            _whooshCloth = Sequence("Whoosh_Cloth", 0f);
            _whooshClothStrong = Sequence("Whoosh_Cloth_Strong", 0.4f);
            _whooshGear = Sequence("Whoosh_Gear", 0f);
            _whooshGearStrong = Sequence("Whoosh_Gear_Strong", 0.4f);

            _tremorShake = Loop("Wind_Shake_Loop");
            _tremorWind = Loop("Wind_Run_Loop");

            _oneShots.Clear();

            for (int i = 0; i < 16; i++)
            {
                var s = NewSource(false);
                s.volume = 1f;
                _oneShots.Add(s);
            }
        }

        private void Rebuild()
        {
            try
            {
                if (_host != null)
                    Destroy(_host);
            }
            catch
            {
            }

            _host = null;
            _pending.Clear();
            _lastPick.Clear();

            foreach (var pair in _sets)
            {
                foreach (AudioClip clip in pair.Value)
                {
                    try
                    {
                        if (clip != null)
                            Destroy(clip);
                    }
                    catch
                    {
                    }
                }
            }

            _sets.Clear();
            _loadQueue.Clear();
            _scanned = false;
            _built = false;
        }

        private bool _built;

        private bool _on;
        private Transform _player;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _speed;

        private bool _hasLastLook;
        private float _lastYaw, _lastPitch;
        private float _yawRate, _pitchRate;

        private float _highRotation;
        private float _windVolume;
        private float _turnVolume;
        private float _pan;
        private float _interior = 1f;
        private float _nextRoofCheck;
        private float _runBlend;
        private float _tremor;
        private float _tremorVolume;

        private static float FInterpTo(float current, float target, float dt, float speed)
        {
            if (speed <= 0f)
                return target;

            float dist = target - current;

            if (dist * dist < 1e-8f)
                return target;

            return current + dist * Mathf.Clamp01(dt * speed);
        }

        private static float MapRangeClamped(float v, float inA, float inB, float outA, float outB)
        {
            if (Mathf.Approximately(inA, inB))
                return v >= inB ? outB : outA;

            return Mathf.Lerp(outA, outB, Mathf.Clamp01((v - inA) / (inB - inA)));
        }

        private static float CrossFade(float p, float a, float b, float c, float d)
        {
            float fadeIn = b <= a ? (p >= a ? 1f : 0f) : Mathf.Clamp01((p - a) / (b - a));
            float fadeOut = d <= c ? (p <= c ? 1f : 0f) : 1f - Mathf.Clamp01((p - c) / (d - c));
            return fadeIn * fadeOut;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float now = Time.unscaledTime;

            bool inRaid = TrackPlayer(dt);
            _on = inRaid && Plugin.Enabled != null && Plugin.Enabled.Value;

            if (!_on && !_built)
                return;

            if (!_scanned)
                Scan();

            LoadSome();

            if (!_built)
            {
                BuildSources();
                _built = true;
            }

            TrackLook(dt);

            float rate = Mathf.Abs(_yawRate) + Mathf.Abs(_pitchRate);
            _highRotation = FInterpTo(_highRotation, rate, dt, 5f);
            float hr = _highRotation;

            bool windOn = _on && CfgWind.Value && CfgWindVolume.Value > 0.001f;

            if (windOn && now >= _nextRoofCheck)
            {
                _nextRoofCheck = now + 0.1f;
                _interior = MapRangeClamped(RoofDistance(), 2f, 5f, 0.5f, 1f);
            }

            float move = MapRangeClamped(_speed, 1.5f, 3.0f, 0.1f, 3.0f);
            float windTarget = windOn ? MapRangeClamped(rate + move, 0f, 3f, 0f, _interior) : 0f;
            _windVolume = FInterpTo(_windVolume, windTarget, dt, 10f);

            _pan = FInterpTo(_pan, MapRangeClamped(_yawRate, -4f, 4f, -0.6f, 0.6f), dt, 5f);

            _runBlend = FInterpTo(_runBlend, _speed > 1.5f ? 1f : 0f, dt, 4f);

            float w = _windVolume * 0.9f * (CfgWindVolume != null ? CfgWindVolume.Value : 1f);
            _windMic.Tick(this, w * 0.54f, _pan);
            _windRun.Tick(this, w * 0.72f * _runBlend, _pan);
            _windShake.Tick(this, w * 0.27f * CrossFade(hr, 1.5f, 3f, 10f, 10f), _pan);
            _windGust.Tick(this, w * 1.8f * CrossFade(hr, 2.5f, 4f, 100f, 100f), _pan, now);

            bool turnOn = _on && CfgTurn.Value && CfgTurnVolume.Value > 0.001f;
            float turnTarget = turnOn ? MapRangeClamped(rate, 0f, 3f, 0f, 1f) : 0f;
            _turnVolume = FInterpTo(_turnVolume, turnTarget, dt, 10f);

            float t = _turnVolume * (CfgTurnVolume != null ? CfgTurnVolume.Value : 1f);
            float slow = CrossFade(hr, 0f, 0f, 1f, 1.5f);
            _clothLow.Tick(this, t * 0.7f * slow, 0f);
            _clothHigh.Tick(this, t * 1.0f * slow, 0f);
            _clothFast.Tick(this, t * 0.25f * CrossFade(hr, 1f, 3f, 10f, 10f), 0f);

            float mid = CrossFade(hr, 1.5f, 3f, 3.5f, 6f);
            float fast = CrossFade(hr, 3f, 4f, 100f, 100f);
            _whooshCloth.Tick(this, t * 1.0f * mid, 0f, now);
            _whooshClothStrong.Tick(this, t * 0.8f * fast, 0f, now);
            _whooshGear.Tick(this, t * 1.2f * mid, 0f, now);
            _whooshGearStrong.Tick(this, t * 0.96f * fast, 0f, now);

            _tremor = FInterpTo(_tremor, 0f, dt, 3f);
            bool tremorOn = _on && CfgTremor.Value && CfgTremorVolume.Value > 0.001f;
            _tremorVolume = FInterpTo(_tremorVolume, tremorOn ? _tremor : 0f, dt, 10f);

            float tv = _tremorVolume * (CfgTremorVolume != null ? CfgTremorVolume.Value : 1f);
            _tremorShake.Tick(this, tv * 10f, 0f);
            _tremorWind.Tick(this, tv * 2f, 0f);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now)
                    continue;

                Fire(_pending[i]);
                _pending.RemoveAt(i);
            }
        }

        private void Fire(Pending p)
        {
            if (p.Clip == null || _oneShots.Count == 0)
                return;

            AudioSource s = _oneShots[_oneShotNext];
            _oneShotNext = (_oneShotNext + 1) % _oneShots.Count;

            if (s == null)
                return;

            s.pitch = p.Pitch;
            s.PlayOneShot(p.Clip, p.Volume);
        }

        private void Footstep(Player player)
        {
            if (!_on || !_built || CfgStep == null || !CfgStep.Value)
                return;

            float master = CfgStepVolume.Value;

            if (master <= 0.001f)
                return;

            StepCue cue;

            if (PoseLevel(player) < 0.75f)
                cue = Crouch;
            else if (SprintState.IsSprinting(player))
                cue = Run;
            else
                cue = Walk;

            float now = Time.unscaledTime;
            bool groupA = UnityEngine.Random.value < cue.GroupA;
            bool groupB = UnityEngine.Random.value < cue.GroupB;
            var played = new bool[cue.Layers.Length];

            for (int i = 0; i < cue.Layers.Length; i++)
            {
                StepLayer l = cue.Layers[i];

                if (l.Group == 1 && !groupA) continue;
                if (l.Group == 2 && !groupB) continue;
                if (l.With >= 0 && (l.With >= played.Length || !played[l.With])) continue;
                if (UnityEngine.Random.value >= l.Chance) continue;

                int last;
                _lastPick.TryGetValue(l.Set, out last);
                AudioClip clip = Pick(Set(l.Set), ref last);
                _lastPick[l.Set] = last;

                if (clip == null)
                    continue;

                played[i] = true;

                var p = new Pending
                {
                    At = now + UnityEngine.Random.Range(l.DelayMin, l.DelayMax),
                    Clip = clip,
                    Volume = l.Volume * master,
                    Pitch = UnityEngine.Random.Range(l.PitchMin, l.PitchMax)
                };

                if (p.At <= now)
                    Fire(p);
                else
                    _pending.Add(p);
            }
        }

        private bool TrackPlayer(float dt)
        {
            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player == null || (player.HealthController != null && !player.HealthController.IsAlive))
                {
                    _player = null;
                    _hasLastPos = false;
                    _speed = 0f;
                    return false;
                }

                Transform tr = player.GetComponent<Transform>();

                if (tr == null)
                    return false;

                if (tr != _player)
                {
                    _player = tr;
                    _hasLastPos = false;
                }

                Vector3 pos = tr.position;

                if (_hasLastPos && dt > 0.0001f)
                {
                    Vector3 d = pos - _lastPos;
                    d.y = 0f;
                    float raw = Mathf.Min(d.magnitude / dt, 10f);
                    _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-4f * dt));
                }

                _lastPos = pos;
                _hasLastPos = true;
                return true;
            }
            catch (Exception)
            {
                _speed = 0f;
                return false;
            }
        }

        private void TrackLook(float dt)
        {
            Camera main = _on ? Camera.main : null;

            if (main == null || dt <= 0.0001f)
            {
                _hasLastLook = false;
                _yawRate = FInterpTo(_yawRate, 0f, dt, 10f);
                _pitchRate = FInterpTo(_pitchRate, 0f, dt, 10f);
                return;
            }

            Vector3 f = main.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;

            if (_hasLastLook)
            {
                float y = Mathf.Clamp(Mathf.DeltaAngle(_lastYaw, yaw) / dt, -2000f, 2000f) / DegPerRateUnit;
                float p = Mathf.Clamp((pitch - _lastPitch) / dt, -2000f, 2000f) / DegPerRateUnit;
                float k = 1f - Mathf.Exp(-20f * dt);
                _yawRate = Mathf.Lerp(_yawRate, y, k);
                _pitchRate = Mathf.Lerp(_pitchRate, p, k);
            }

            _lastYaw = yaw;
            _lastPitch = pitch;
            _hasLastLook = true;
        }

        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private float RoofDistance()
        {
            try
            {
                Camera main = Camera.main;

                if (main == null)
                    return 5f;

                Vector3 from = main.transform.position;
                int n = Physics.RaycastNonAlloc(from, Vector3.up, _hits, 5f, ~0, QueryTriggerInteraction.Ignore);
                float best = 5f;
                Transform root = _player != null ? _player.root : null;

                for (int i = 0; i < n; i++)
                {
                    Collider c = _hits[i].collider;

                    if (c == null)
                        continue;

                    if (root != null && c.transform.root == root)
                        continue;

                    if (_hits[i].distance < best)
                        best = _hits[i].distance;
                }

                return best;
            }
            catch
            {
                return 5f;
            }
        }

        private static PropertyInfo _mcProp;
        private static PropertyInfo _poseProp;
        private static bool _poseLookedUp;

        private static float PoseLevel(Player player)
        {
            try
            {
                if (!_poseLookedUp)
                {
                    _poseLookedUp = true;
                    _mcProp = typeof(Player).GetProperty("MovementContext",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (_mcProp != null)
                        _poseProp = _mcProp.PropertyType.GetProperty("PoseLevel",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (_poseProp == null)
                        Plugin.Log.LogWarning("[BodyCamera] MovementContext.PoseLevel not found; crouch step sounds use the walk set.");
                }

                if (_poseProp == null || player == null)
                    return 1f;

                object mc = _mcProp.GetValue(player, null);

                if (mc == null)
                    return 1f;

                return Convert.ToSingle(_poseProp.GetValue(mc, null));
            }
            catch
            {
                return 1f;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}

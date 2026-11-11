using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamBeep
    {
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgVolume;

        private const float OffPitch = 0.75f;

        private static AudioSource _src;

        private static AudioSource _adjustSrc;
        private static readonly AudioClip[] _adjustClips = new AudioClip[4];
        private static bool _adjustLoaded;
        private static AudioClip _clip;
        private static AudioClip _recClip;
        private static bool _loadFailed;
        private static bool _recLoaded;

        private static float _pendingAt = -1f;

        internal static void Schedule(float delay)
        {
            _pendingAt = Time.unscaledTime + delay;
        }

        internal static void Tick()
        {
            if (_pendingAt < 0f || Time.unscaledTime < _pendingAt)
                return;

            _pendingAt = -1f;

            if (Plugin.Enabled.Value)
            {
                Play(true);
                Plugin.Log.LogInfo("[BodyCamera] Raid start beep.");
            }
        }

        internal static void ResetAudio()
        {
            try
            {
                if (_src != null)
                    UnityEngine.Object.Destroy(_src.gameObject);
            }
            catch
            {
            }

            try
            {
                if (_adjustSrc != null)
                    UnityEngine.Object.Destroy(_adjustSrc.gameObject);
            }
            catch
            {
            }

            _adjustSrc = null;
            _adjustLoaded = false;

            for (int i = 0; i < _adjustClips.Length; i++)
                _adjustClips[i] = null;

            _src = null;
            _clip = null;
            _recClip = null;
            _recLoaded = false;
            _loadFailed = false;
        }

        internal static void BindConfig()
        {
            CfgEnabled = ConfigMigration.Bind(
                Plugin.SecOverlay, "Toggle Sound", true,
                "Beep when BodyCam turns on, lower beep when it turns off.",
                null, 74, Plugin.SecGeneral, "Toggle Sound");

            CfgVolume = ConfigMigration.Bind(
                Plugin.SecOverlay, "Toggle Sound Volume", 0.70f,
                "Volume of the camera beep.",
                new AcceptableValueRange<float>(0f, 1f), 73, Plugin.SecGeneral, "Toggle Sound Volume");
        }

        internal static void Play(bool on)
        {
            try
            {
                if (CfgEnabled == null || !CfgEnabled.Value || CfgVolume.Value <= 0.001f)
                    return;

                if (!Ensure())
                    return;

                _src.Stop();
                _src.pitch = on ? 1f : OffPitch;
                _src.PlayOneShot(_clip, Mathf.Clamp01(CfgVolume.Value));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Beep error: " + ex.Message);
            }
        }

        internal static void PlayAdjust()
        {
            try
            {
                if (CfgEnabled == null || !CfgEnabled.Value || CfgVolume.Value <= 0.001f)
                    return;

                if (_adjustSrc == null || !_adjustSrc.isActiveAndEnabled)
                {
                    if (_adjustSrc != null)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(_adjustSrc.gameObject);
                        }
                        catch
                        {
                        }
                    }

                    var go = new GameObject("BodyCamera_Adjust");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.HideAndDontSave;

                    _adjustSrc = go.AddComponent<AudioSource>();
                    _adjustSrc.loop = false;
                    _adjustSrc.playOnAwake = false;
                    _adjustSrc.spatialBlend = 0f;
                    _adjustSrc.priority = 0;
                    _adjustSrc.ignoreListenerPause = true;
                    _adjustSrc.ignoreListenerVolume = true;
                    _adjustSrc.bypassReverbZones = true;
                    _adjustSrc.bypassEffects = true;
                    _adjustSrc.dopplerLevel = 0f;
                }

                if (!_adjustLoaded)
                {
                    _adjustLoaded = true;

                    for (int i = 0; i < _adjustClips.Length; i++)
                        _adjustClips[i] = LoadClip("adjust" + (i + 1) + ".wav");
                }

                AudioClip clip = _adjustClips[UnityEngine.Random.Range(0, _adjustClips.Length)];

                if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
                {
                    _adjustLoaded = false;
                    return;
                }

                _adjustSrc.Stop();
                _adjustSrc.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
                _adjustSrc.PlayOneShot(clip, 0.8f * Mathf.Clamp01(CfgVolume.Value));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Adjust sound error: " + ex.Message);
            }
        }

        internal static void PlayOverlay()
        {
            try
            {
                if (CfgVolume == null || CfgVolume.Value <= 0.001f)
                    return;

                if (!Ensure())
                    return;

                if (!_recLoaded)
                {
                    _recLoaded = true;
                    _recClip = LoadClip("rec.wav");
                }

                if (_recClip == null)
                    return;

                _src.pitch = 1f;
                _src.PlayOneShot(_recClip, Mathf.Clamp01(CfgVolume.Value));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Overlay sound error: " + ex.Message);
            }
        }

        private static bool Ensure()
        {
            if (_src != null && _clip != null &&
                _src.isActiveAndEnabled &&
                _clip.loadState == AudioDataLoadState.Loaded)
                return true;

            if (_src != null || _clip != null)
                ResetAudio();

            if (_loadFailed)
                return false;

            _clip = LoadClip("beep.wav");

            if (_clip == null)
            {
                _loadFailed = true;
                return false;
            }

            var go = new GameObject("BodyCamera_Beep");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;

            _src = go.AddComponent<AudioSource>();
            _src.loop = false;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.priority = 0;

            _src.ignoreListenerPause = true;
            _src.ignoreListenerVolume = true;
            _src.bypassReverbZones = true;
            _src.bypassEffects = true;
            _src.dopplerLevel = 0f;

            return true;
        }

        private static AudioClip LoadClip(string file)
        {
            try
            {
                var asm = typeof(BodyCamBeep).Assembly;
                string name = null;

                foreach (string n in asm.GetManifestResourceNames())
                    if (n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                        n.Equals(file, StringComparison.OrdinalIgnoreCase))
                        name = n;

                if (name == null)
                {
                    Plugin.Log.LogWarning("[BodyCamera] Sound " + file + " not found in the DLL.");
                    return null;
                }

                byte[] data;

                using (Stream s = asm.GetManifestResourceStream(name))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }

                int channels = 1, rate = 44100, bits = 16;
                int pos = 12;

                while (pos + 8 <= data.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    int size = BitConverter.ToInt32(data, pos + 4);
                    int body = pos + 8;

                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(data, body + 2);
                        rate = BitConverter.ToInt32(data, body + 4);
                        bits = BitConverter.ToInt16(data, body + 14);
                    }
                    else if (id == "data")
                    {
                        if (bits != 16)
                        {
                            Plugin.Log.LogWarning("[BodyCamera] Beep sound must be 16-bit PCM.");
                            return null;
                        }

                        size = Math.Min(size, data.Length - body);
                        int count = size / 2;
                        var samples = new float[count];

                        for (int i = 0; i < count; i++)
                            samples[i] = BitConverter.ToInt16(data, body + i * 2) / 32768f;

                        var clip = AudioClip.Create("bc_" + file, count / channels, channels, rate, false);
                        clip.SetData(samples, 0);
                        return clip;
                    }

                    pos = body + size + (size & 1);
                }

                Plugin.Log.LogWarning("[BodyCamera] Beep sound: no audio data.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Beep sound error: " + ex.Message);
            }

            return null;
        }
    }
}

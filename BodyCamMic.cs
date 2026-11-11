using System;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal sealed class BodyCamMic : MonoBehaviour
    {
        internal static ConfigEntry<float> CfgMic;

        internal static void BindConfig()
        {
            CfgMic = ConfigMigration.Bind(
                Plugin.SecSounds, "Camera Mic", 0.00f,
                "Game sound as heard through the camera microphone (0 = off).",
                new AcceptableValueRange<float>(0f, 1f), 60, null, null);
        }

        private static GameObject _host;
        private static AudioListener _listener;
        private static BodyCamMic _filter;
        private static float _nextFind;
        private static float _nextLog;

        internal static void Create()
        {
            if (_host != null) return;
            _host = new GameObject("BodyCamera_Mic");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<BodyCamMicDriver>();
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            float target = 0f;

            try
            {
                bool inRaid = Singleton<GameWorld>.Instance?.MainPlayer != null;

                if (inRaid && Plugin.Enabled != null && Plugin.Enabled.Value && CfgMic != null)
                    target = Mathf.Clamp01(CfgMic.Value);
            }
            catch
            {
                target = 0f;
            }

            if (_listener == null || !_listener.isActiveAndEnabled)
            {
                _listener = null;
                _filter = null;

                if (target <= 0f || now < _nextFind)
                    return;

                _nextFind = now + 1f;
                AudioListener[] all = FindObjectsOfType<AudioListener>();

                foreach (AudioListener l in all)
                {
                    if (l != null && l.isActiveAndEnabled)
                    {
                        _listener = l;
                        break;
                    }
                }

                if (_listener == null)
                    return;

                _filter = _listener.GetComponent<BodyCamMic>() ?? _listener.gameObject.AddComponent<BodyCamMic>();
                _filter._sampleRate = AudioSettings.outputSampleRate;

                if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo("[BodyCamera] Camera mic on listener '" + _listener.name +
                                       "', " + _filter._sampleRate + " Hz, listeners " + all.Length);
            }

            if (_filter == null)
                return;

            _filter._target = target;

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value && target > 0f && now >= _nextLog)
            {
                _nextLog = now + 2f;
                Plugin.Log.LogInfo(string.Format(
                    "[BodyCamera] Camera mic: mix {0:0.00}, gain {1:0.0} dB, calls {2}, ch {3}",
                    _filter._mix, 20f * Mathf.Log10(Mathf.Max((float)_filter._agcGain, 1e-4f)),
                    _filter._calls, _filter._channels));
            }
        }

        private volatile float _target;
        private volatile int _sampleRate = 48000;
        private float _mix;
        private int _calls;
        private int _channels;
        private int _builtRate;
        private bool _active;

        private Biquad[][] _capsule;
        private Biquad[][] _codec;
        private double[][] _pinkX;
        private double[][] _pinkY;
        private readonly System.Random _rng = new System.Random(1);

        private int _blockLen;
        private int _blockPos;
        private float _blockPeak;
        private double _env = 1e-4;
        private int _hold;
        private double _agcGain = 1.0;
        private double _gainFrom = 1.0;
        private double _gainStep;

        private const double Width = 0.45;
        private const double Drive = 2.2;
        private const double ClipOut = 0.75;
        private static readonly double HissGain = Math.Pow(10.0, -62.0 / 20.0) / 0.1 * Math.Sqrt(12.0);
        private static readonly double AgcTarget = Math.Pow(10.0, -14.0 / 20.0);
        private static readonly double AgcMax = Math.Pow(10.0, 20.0 / 20.0);
        private static readonly double Makeup = Math.Pow(10.0, 6.5 / 20.0);

        private static readonly double[] PinkB = { 0.049922035, -0.095993537, 0.050612699, -0.004408786 };
        private static readonly double[] PinkA = { 1.0, -2.494956002, 2.017265875, -0.522189400 };

        private void Build(int rate, int channels)
        {
            _builtRate = rate;
            _channels = channels;
            _capsule = new Biquad[channels][];
            _codec = new Biquad[channels][];
            _pinkX = new double[channels][];
            _pinkY = new double[channels][];

            for (int c = 0; c < channels; c++)
            {
                _capsule[c] = new[]
                {
                    Biquad.HighPass(rate, 200, 0.70710678),
                    Biquad.HighPass(rate, 200, 0.70710678),
                    Biquad.LowPass(rate, 7500, 0.54119610),
                    Biquad.LowPass(rate, 7500, 1.30656296),
                    Biquad.Peak(rate, 2800, 4.0, 0.9),
                    Biquad.Peak(rate, 350, -3.0, 1.0),
                };
                _codec[c] = new[]
                {
                    Biquad.LowPass(rate, 7000, 0.51763809),
                    Biquad.LowPass(rate, 7000, 0.70710678),
                    Biquad.LowPass(rate, 7000, 1.93185165),
                };
                _pinkX[c] = new double[4];
                _pinkY[c] = new double[4];
            }

            _blockLen = Math.Max(1, rate / 1000);
            ResetDynamics();
        }

        private void ResetDynamics()
        {
            _blockPos = 0;
            _blockPeak = 0f;
            _env = 1e-4;
            _hold = 0;
            _agcGain = 1.0;
            _gainFrom = 1.0;
            _gainStep = 0.0;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            _calls++;

            float target = _target;
            int rate = _sampleRate;

            if (!_active && target <= 0f && _mix <= 0f)
                return;

            if (_capsule == null || rate != _builtRate || channels != _channels)
                Build(rate, channels);

            if (!_active)
            {
                Build(rate, channels);
                _active = true;
            }

            int frames = data.Length / channels;
            float mixStep = 1f / (0.3f * rate);
            bool stereo = channels == 2;

            for (int f = 0; f < frames; f++)
            {
                int i = f * channels;

                double l = data[i];
                double r = stereo ? data[i + 1] : 0.0;
                double wl = l, wr = r;

                if (stereo)
                {
                    double mid = (l + r) * 0.5, side = (l - r) * 0.5;
                    wl = mid + Width * side;
                    wr = mid - Width * side;
                }

                double g = _gainFrom + _gainStep * _blockPos;
                float peak = _blockPeak;

                for (int c = 0; c < channels; c++)
                {
                    double v = c == 0 ? wl : (c == 1 && stereo ? wr : data[i + c]);

                    Biquad[] cap = _capsule[c];
                    for (int k = 0; k < cap.Length; k++)
                        v = cap[k].Process(v);

                    v *= Drive;
                    v = (v > 0 ? Math.Tanh(v * 1.1) / 1.1 : Math.Tanh(v * 0.9) / 0.9) * ClipOut;

                    v += Pink(c);

                    float a = (float)Math.Abs(v);
                    if (a > peak) peak = a;

                    v *= g * Makeup;
                    Biquad[] cod = _codec[c];
                    for (int k = 0; k < cod.Length; k++)
                        v = cod[k].Process(v);

                    v = SoftLimit(v);

                    data[i + c] = (float)(data[i + c] * (1f - _mix) + v * _mix);
                }

                _blockPeak = peak;

                if (++_blockPos >= _blockLen)
                    EndBlock();

                if (_mix < target) _mix = Mathf.Min(target, _mix + mixStep);
                else if (_mix > target) _mix = Mathf.Max(target, _mix - mixStep);
            }

            if (target <= 0f && _mix <= 0f)
                _active = false;
        }

        private void EndBlock()
        {
            double lvl = _blockPeak;

            if (lvl > _env)
            {
                _env = lvl;
                _hold = 150;
            }
            else if (_hold > 0)
            {
                _hold--;
            }
            else
            {
                _env = _env * 0.9965 + lvl * 0.0035;
            }

            double want = Math.Min(AgcMax, AgcTarget / Math.Max(_env, 1e-6));
            double k = want < _agcGain ? 0.5 : 0.0012;
            double next = _agcGain + (want - _agcGain) * k;

            _gainFrom = _agcGain;
            _gainStep = (next - _agcGain) / _blockLen;
            _agcGain = next;
            _blockPos = 0;
            _blockPeak = 0f;
        }

        private double Pink(int c)
        {
            double[] x = _pinkX[c], y = _pinkY[c];
            x[3] = x[2]; x[2] = x[1]; x[1] = x[0];
            x[0] = (_rng.NextDouble() - 0.5) * HissGain;
            y[3] = y[2]; y[2] = y[1]; y[1] = y[0];
            y[0] = PinkB[0] * x[0] + PinkB[1] * x[1] + PinkB[2] * x[2] + PinkB[3] * x[3]
                   - PinkA[1] * y[1] - PinkA[2] * y[2] - PinkA[3] * y[3];
            return y[0];
        }

        private static double SoftLimit(double v)
        {
            const double knee = 0.7, room = 0.28;
            double a = Math.Abs(v);
            if (a <= knee) return v;
            double o = knee + room * Math.Tanh((a - knee) / room);
            return v < 0 ? -o : o;
        }

        private sealed class Biquad
        {
            private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

            internal double Process(double x)
            {
                double y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }

            private static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                return new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };
            }

            internal static Biquad LowPass(int rate, double f, double q)
            {
                f = Math.Min(f, rate * 0.45);
                double w = 2 * Math.PI * f / rate, cw = Math.Cos(w), al = Math.Sin(w) / (2 * q);
                return Make((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + al, -2 * cw, 1 - al);
            }

            internal static Biquad HighPass(int rate, double f, double q)
            {
                double w = 2 * Math.PI * f / rate, cw = Math.Cos(w), al = Math.Sin(w) / (2 * q);
                return Make((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + al, -2 * cw, 1 - al);
            }

            internal static Biquad Peak(int rate, double f, double gainDb, double q)
            {
                double A = Math.Pow(10, gainDb / 40), w = 2 * Math.PI * f / rate, cw = Math.Cos(w), al = Math.Sin(w) / (2 * q);
                return Make(1 + al * A, -2 * cw, 1 - al * A, 1 + al / A, -2 * cw, 1 - al / A);
            }
        }
    }

    internal sealed class BodyCamMicDriver : MonoBehaviour
    {
        private void Update()
        {
            BodyCamMic.Tick();
        }
    }
}

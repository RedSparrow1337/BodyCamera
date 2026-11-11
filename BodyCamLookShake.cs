using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamLookShake
    {
        private struct Inst
        {
            public bool On;
            public float T, S, PhP, OffY, OffR;
        }

        private const int MaxInst = 48;
        private const float Dur = 0.2f;
        private const float BlendIn = 0.1f;
        private const float BlendOut = 0.1f;
        private const float Step = 1f / 60f;
        private const float DegPerUnit = 87.5f;

        private static readonly Inst[] _inst = new Inst[MaxInst];
        private static readonly System.Random _rng = new System.Random();

        private static bool _has;
        private static float _lastYaw, _lastPitch, _acc;

        internal static void Reset()
        {
            for (int i = 0; i < MaxInst; i++)
                _inst[i].On = false;

            _has = false;
            _acc = 0f;
        }

        internal static Vector3 Update(float aimYaw, float aimPitch, float speed, float strength)
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.1f);

            if (!_has)
            {
                _has = true;
                _lastYaw = aimYaw;
                _lastPitch = aimPitch;
            }

            float yawRate = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, aimYaw)) / dt;
            float pitchRate = Mathf.Abs(aimPitch - _lastPitch) / dt;

            _lastYaw = aimYaw;
            _lastPitch = aimPitch;

            _acc += dt;
            int spawns = 0;

            while (_acc >= Step && spawns < 3)
            {
                _acc -= Step;
                spawns++;
            }

            if (_acc > Step)
                _acc = Step;

            if (strength > 0.0001f && yawRate + pitchRate > 0.5f)
            {
                float look = (yawRate + pitchRate) / DegPerUnit;
                float scale =
                    (MapClamp(look, 1f, 4f, 0f, 3f) +
                     MapClamp(speed * 100f, 0f, 600f, 0f, 1.5f)) * strength;

                if (scale > 0.001f)
                {
                    for (int s = 0; s < spawns; s++)
                        Spawn(scale);
                }
            }

            Vector3 sum = Vector3.zero;

            for (int i = 0; i < MaxInst; i++)
            {
                if (!_inst[i].On)
                    continue;

                _inst[i].T += dt;
                float t = _inst[i].T;

                if (t >= Dur)
                {
                    _inst[i].On = false;
                    continue;
                }

                float w = Mathf.Min(1f, t / BlendIn) * Mathf.Clamp01((Dur - t) / BlendOut) * _inst[i].S;

                sum.x += w * 0.3f * Mathf.Sin(_inst[i].PhP + 2f * Mathf.PI * 0.1f * t);
                sum.y += w * 0.2f * Perlin(_inst[i].OffY + 2f * t);
                sum.z += w * 0.5f * Perlin(_inst[i].OffR + 2f * t);
            }

            if (float.IsNaN(sum.x) || float.IsNaN(sum.y) || float.IsNaN(sum.z))
            {
                Reset();
                return Vector3.zero;
            }

            return sum;
        }

        private static void Spawn(float scale)
        {
            for (int i = 0; i < MaxInst; i++)
            {
                if (_inst[i].On)
                    continue;

                _inst[i].On = true;
                _inst[i].T = 0f;
                _inst[i].S = scale;
                _inst[i].PhP = (float)(_rng.NextDouble() * 2.0 * Mathf.PI);
                _inst[i].OffY = (float)(_rng.NextDouble() * 100.0);
                _inst[i].OffR = (float)(_rng.NextDouble() * 100.0);
                return;
            }
        }

        private static float Perlin(float x)
        {
            return (Mathf.PerlinNoise(x, 0.37f) - 0.5f) * 2f;
        }

        private static float MapClamp(float v, float a, float b, float c, float d)
        {
            return Mathf.Lerp(c, d, Mathf.InverseLerp(a, b, v));
        }
    }
}

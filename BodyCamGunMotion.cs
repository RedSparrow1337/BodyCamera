using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamGunMotion
    {
        private const float WalkStepsPerLoop = 6f;

        private static float _idleTime;
        private static float _shakeTime;
        private static float _walkSteps;
        private static float _shakeAlpha = 0.1f;
        private static float _gait;
        private static int _lastStepCount = -1;

        private static float _walkAmp = 0.5f;
        private static float _nextLog;

        private static float _phaseError;

        private const float OutputSmoothing = 20f;
        private static Vector3 _outPos;
        private static Vector3 _outRot;
        private static bool _hasOut;
        private static int _outFrame = -1;

        internal static void Reset()
        {
            _idleTime = 0f;
            _shakeTime = 0f;
            _walkSteps = 0f;
            _shakeAlpha = 0.1f;
            _gait = 0f;
            _walkAmp = 0.5f;
            _lastStepCount = -1;
            _phaseError = 0f;
            _outPos = Vector3.zero;
            _outRot = Vector3.zero;
            _hasOut = false;
            _outFrame = -1;
        }

        private static float PaceK(float speed, bool sprinting, float aim)
        {
            float hip = sprinting
                ? V(Plugin.WalkSprint)
                : Pace(speed, 0.8f, 1.6f, 2.4f, V(Plugin.WalkSlow), V(Plugin.WalkNormal), V(Plugin.WalkFast));
            float ads = Pace(speed, 0.5f, 1.0f, 1.5f,
                V(Plugin.AdsWalkSlow), V(Plugin.AdsWalkNormal), V(Plugin.AdsWalkFast));
            return Mathf.Lerp(hip, ads, Mathf.Clamp01(aim));
        }

        private static float Pace(float speed, float s0, float s1, float s2, float k0, float k1, float k2)
        {
            if (speed <= s1)
                return Mathf.Lerp(k0, k1, Mathf.InverseLerp(s0, s1, speed));
            return Mathf.Lerp(k1, k2, Mathf.InverseLerp(s1, s2, speed));
        }

        private static float V(BepInEx.Configuration.ConfigEntry<float> e)
        {
            return e != null ? Mathf.Max(0f, e.Value) : 1f;
        }

        internal static void Tick(float dt, float speed, bool airborne, float aim, bool sprinting)
        {
            float ampTarget = sprinting
                ? 1f
                : Mathf.Lerp(0.4f, 0.65f, Mathf.InverseLerp(0.6f, 2.5f, speed));

            float paceK = PaceK(speed, sprinting, aim);
            ampTarget *= paceK;
            _walkAmp = Mathf.Lerp(_walkAmp, ampTarget, 1f - Mathf.Exp(-3f * dt));

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value && speed > 0.15f &&
                Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo(
                    "[BodyCamera] Gun walk: speed " + speed.ToString("0.00") + " m/s, sprint " + sprinting +
                    ", pace x" + paceK.ToString("0.00") + ", strength " + _walkAmp.ToString("0.00") + ".");
            }

            _idleTime += dt;
            _shakeTime += dt;

            float idlePeriod = BodyCamGunCurves.AkIdle.Period;
            float shakePeriod = BodyCamGunCurves.AkShake.Period;
            if (_idleTime >= idlePeriod) _idleTime -= idlePeriod;
            if (_shakeTime >= shakePeriod) _shakeTime -= shakePeriod;

            float gaitFull = Mathf.Lerp(1.0f, 0.7f, Mathf.Clamp01(aim));
            float gaitTarget = airborne ? 0f : Mathf.InverseLerp(0.2f, gaitFull, speed);
            float gaitRate = gaitTarget > _gait ? 4f : 5f;
            _gait = Mathf.Lerp(_gait, gaitTarget, 1f - Mathf.Exp(-gaitRate * dt));

            float shakeTarget = Mathf.Lerp(0.1f, 1f, Mathf.Clamp01(speed));
            _shakeAlpha = Mathf.Lerp(_shakeAlpha, shakeTarget, 1f - Mathf.Exp(-10f * dt));

            int stepCount;
            float interval;
            bool fresh = BodyCamLateUpdatePatch.GetGunStepInfo(out stepCount, out interval);

            if (!fresh)
            {
                interval = Mathf.Lerp(0.65f, 0.35f, Mathf.InverseLerp(1f, 6f, speed));
            }

            if (speed > 0.15f && !airborne)
                _walkSteps += dt / Mathf.Clamp(interval, 0.25f, 1.2f);

            if (fresh && stepCount != _lastStepCount)
            {
                if (_lastStepCount >= 0)
                    _phaseError = _walkSteps - Mathf.Round(_walkSteps);

                _lastStepCount = stepCount;
            }

            if (_phaseError != 0f)
            {
                float corr = _phaseError * (1f - Mathf.Exp(-6f * dt));
                _walkSteps -= corr;
                _phaseError -= corr;

                if (Mathf.Abs(_phaseError) < 0.0005f)
                    _phaseError = 0f;
            }

            if (_walkSteps >= WalkStepsPerLoop) _walkSteps -= WalkStepsPerLoop;
            if (_walkSteps < 0f) _walkSteps += WalkStepsPerLoop;
        }

        internal static void Evaluate(bool pistol, float idleK, float walkK,
                                      out Vector3 pos, out Vector3 rot)
        {
            pos = Vector3.zero;
            rot = Vector3.zero;

            var idle = pistol ? BodyCamGunCurves.GlockIdle : BodyCamGunCurves.AkIdle;
            var shake = pistol ? BodyCamGunCurves.GlockShake : BodyCamGunCurves.AkShake;
            var walk = pistol ? BodyCamGunCurves.GlockWalk : BodyCamGunCurves.AkWalk;

            idle.AddSample(_idleTime, idleK, ref pos, ref rot);

            shake.AddSample(_shakeTime, _shakeAlpha * Mathf.Lerp(idleK, walkK * _walkAmp, _gait), ref pos, ref rot);

            walk.AddSample(_walkSteps / WalkStepsPerLoop * walk.Period, _gait * walkK * _walkAmp, ref pos, ref rot);

            int frame = Time.frameCount;

            if (frame != _outFrame)
            {
                _outFrame = frame;

                if (!_hasOut)
                {
                    _outPos = pos;
                    _outRot = rot;
                    _hasOut = true;
                }
                else
                {
                    float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);
                    float k = 1f - Mathf.Exp(-OutputSmoothing * dt);
                    _outPos = Vector3.Lerp(_outPos, pos, k);
                    _outRot = Vector3.Lerp(_outRot, rot, k);
                }
            }

            pos = _outPos;
            rot = _outRot;
        }
    }
}

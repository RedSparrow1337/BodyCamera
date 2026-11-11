using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamHit
    {
        private const float ShakeTime = 0.3f;
        private const float BlendIn = 0.1f;
        private const float BlendOut = 0.2f;
        private const float ShakePitch = 10f;
        private const float ShakeRoll = 10f;
        private const float ShakeYaw = 5f;
        private const float ShakeFreq = 5f;
        private const float ShakeYawFreq = 0.1f;

        private const float PushPeak = 4f;
        private const float PushFreq = 2.2f;
        private const float PushDamping = 0.45f;
        private const float PushTime = 1.5f;

        private const float ZeroDamageAs = 40f;

        private const float ExRadius = 50f;
        private const float ExTime = 1.0f;
        private const float ExBlendIn = 0.1f;
        private const float ExBlendOut = 1.5f;
        private const float ExPitch = 10f, ExPitchW = 15f;
        private const float ExYaw = 4f, ExYawW = 5f;
        private const float ExRoll = 5f, ExRollW = 2f;

        private static float _start = -100f;
        private static float _scale;
        private static float _side = 1f;
        private static readonly float[] _grad = new float[3 * 64];

        private static float _exStart = -100f;
        private static float _exScale;
        private static readonly float[] _exPhase = new float[3];

        private static int _logged;
        private static bool _subscribed;

        internal static void Reset()
        {
            _start = -100f;
            _scale = 0f;
            _exStart = -100f;
            _exScale = 0f;
        }

        internal static void Subscribe()
        {
            if (_subscribed)
                return;

            _subscribed = true;
            ExplosionSharedMethods.OnExplosion += OnExplosion;
        }

        internal static void OnHit(float damage, Vector3 direction)
        {
            float k = Plugin.HitImpact != null ? Mathf.Max(0f, Plugin.HitImpact.Value) : 0f;

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value && _logged < 10)
            {
                _logged++;
                Plugin.Log.LogInfo("[BodyCamera] Hit impact: damage " + damage.ToString("0.0") + ".");
            }

            if (k < 0.0001f)
                return;

            if (damage <= 0f)
                damage = ZeroDamageAs;

            float scale = Mathf.Clamp01(damage / 100f) * k;

            if (Time.time - _start < 0.05f)
            {
                _scale = Mathf.Max(_scale, scale);
                return;
            }

            _scale = scale;
            _start = Time.time;

            float side = 0f;
            Camera cam = Camera.main;

            if (cam != null && direction.sqrMagnitude > 0.0001f)
                side = Vector3.Dot(direction.normalized, cam.transform.right);

            _side = Mathf.Abs(side) > 0.1f ? Mathf.Sign(side) : (Random.value < 0.5f ? -1f : 1f);

            for (int i = 0; i < _grad.Length; i++)
                _grad[i] = Random.Range(-1f, 1f);

            BodyCamAdjust.NoteCombat();
        }

        private const float FallFrom = 0.5f;
        private const float FallFull = 3.0f;

        internal static void OnFall(float fallHeight)
        {
            float k = Plugin.FallImpact != null ? Mathf.Max(0f, Plugin.FallImpact.Value) : 0f;

            if (k < 0.0001f || fallHeight <= FallFrom)
                return;

            float scale = Mathf.Clamp01((fallHeight - FallFrom) / (FallFull - FallFrom)) * k;

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo("[BodyCamera] Fall impact: " + fallHeight.ToString("0.00") + " m, scale " + scale.ToString("0.00") + ".");

            StartShake(scale);
        }

        private static void StartShake(float scale)
        {
            if (scale <= 0.0001f)
                return;

            _exStart = Time.time;
            _exScale = scale;

            for (int i = 0; i < _exPhase.Length; i++)
                _exPhase[i] = Random.Range(0f, 2f * Mathf.PI);
        }

        private static void OnExplosion(Vector3 position, float radius)
        {
            try
            {
                if (Plugin.Enabled == null || !Plugin.Enabled.Value)
                    return;

                float k = Plugin.ExplosionImpact != null ? Mathf.Max(0f, Plugin.ExplosionImpact.Value) : 0f;

                if (k < 0.0001f)
                    return;

                Player player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player == null)
                    return;

                Camera cam = Camera.main;
                Vector3 at = cam != null ? cam.transform.position : player.GetComponent<Transform>().position;
                float dist = Vector3.Distance(position, at);
                float scale = Mathf.Clamp01(1f - dist / ExRadius) * k;

                if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo("[BodyCamera] Explosion impact: " + dist.ToString("0.0") + " m, scale " + scale.ToString("0.00") + ".");

                if (scale <= 0.0001f)
                    return;

                StartShake(scale);

                BodyCamAdjust.NoteCombat();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Explosion impact error: " + ex.Message);
            }
        }

        internal static Vector3 Evaluate()
        {
            float pitch = 0f, yaw = 0f, roll = 0f;

            float t = Time.time - _start;

            if (t >= 0f && t <= PushTime && _scale > 0f)
            {
                float w = 2f * Mathf.PI * PushFreq;
                float wd = w * Mathf.Sqrt(1f - PushDamping * PushDamping);
                float kick = Mathf.Exp(-PushDamping * w * t) * Mathf.Sin(wd * t) * PushPeak / 0.75f * _scale;

                pitch += kick * 0.8f;
                yaw += _side * kick;
                roll -= _side * kick * 0.6f;

                if (t < ShakeTime)
                {
                    float blend = Mathf.Min(1f, Mathf.Min(t / BlendIn, (ShakeTime - t) / BlendOut));
                    float s = _scale * blend;

                    pitch += ShakePitch * s * Noise(0, t * ShakeFreq);
                    roll += ShakeRoll * s * Noise(1, t * ShakeFreq);
                    yaw += ShakeYaw * s * Noise(2, t * ShakeYawFreq + 0.37f);
                }
            }

            float te = Time.time - _exStart;

            if (te >= 0f && te <= ExTime && _exScale > 0f)
            {
                float blend = Mathf.Min(1f, Mathf.Min(te / ExBlendIn, (ExTime - te) / ExBlendOut));
                float s = _exScale * blend;

                pitch += ExPitch * s * Mathf.Sin(_exPhase[0] + ExPitchW * te);
                yaw += ExYaw * s * Mathf.Sin(_exPhase[1] + ExYawW * te);
                roll += ExRoll * s * Mathf.Sin(_exPhase[2] + ExRollW * te);
            }

            return new Vector3(pitch, yaw, roll);
        }

        private static float Noise(int channel, float x)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float u = f * f * (3f - 2f * f);
            float g0 = _grad[channel * 64 + (i & 63)];
            float g1 = _grad[channel * 64 + ((i + 1) & 63)];
            float a = g0 * f;
            float b = g1 * (f - 1f);
            return (a + (b - a) * u) * 2f;
        }
    }

    internal sealed class BodyCamHitShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            foreach (MethodInfo m in typeof(Player).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "ApplyShot" && m.DeclaringType == typeof(Player))
                    return m;
            }

            return null;
        }

        [PatchPrefix]
        private static void Prefix(Player __instance, DamageInfo damageInfo)
        {
            try
            {
                var main = Singleton<GameWorld>.Instance?.MainPlayer;

                if (main == null || !ReferenceEquals(main, __instance))
                    return;

                switch (damageInfo.DamageType)
                {
                    case EDamageType.Bullet:
                    case EDamageType.Sniper:
                    case EDamageType.Melee:
                    case EDamageType.Blunt:
                    case EDamageType.Btr:
                        BodyCamHit.OnHit(damageInfo.Damage, damageInfo.Direction);
                        break;
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class BodyCamFallPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod(
                "OnGrounded",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix(Player __instance, float fallHeight)
        {
            try
            {
                var main = Singleton<GameWorld>.Instance?.MainPlayer;

                if (main != null && ReferenceEquals(main, __instance))
                    BodyCamHit.OnFall(fallHeight);
            }
            catch
            {
            }
        }
    }
}

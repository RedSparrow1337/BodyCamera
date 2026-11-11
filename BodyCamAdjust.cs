using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamAdjust
    {
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgStrength;
        internal static ConfigEntry<float> CfgTiltUp;
        internal static ConfigEntry<bool> CfgHand;
        internal static ConfigEntry<bool> CfgRandom;

        private const float RandomMin = 180f;
        private const float RandomMax = 420f;
        private const float CombatQuiet = 30f;
        private const float CalmNeeded = 4f;
        private static float _nextRandom = -1f;
        private static float _calmSince = -1f;
        private static float _lastCombat = -100f;
        private static readonly Dictionary<Type, PropertyInfo> _idling = new Dictionary<Type, PropertyInfo>();
        private static bool _loggedIdling;

        private const float FrameTime = 1f / 30f;
        private const float OffSwitchTime = 1.65f;

        private static readonly float[][][] Curves =
        {
            new[]
            {
                new[] { -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -3.60f, -7.22f, -7.30f, -6.58f, -4.48f, -2.73f, -1.24f, -0.10f, 1.69f, 4.24f, 5.53f, 4.63f, 4.14f, 4.31f, 4.67f, 5.06f, 5.36f, 5.52f, 5.21f, 3.82f, 0.72f, -4.46f, -9.02f, -10.89f, -10.45f, -7.18f, -2.94f, -2.06f, -2.80f, -2.65f, -1.92f, -1.08f, -0.36f, 0.00f },
                new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.17f, -0.29f, -0.06f, 0.48f, 1.15f, 1.58f, 1.58f, 1.37f, 0.86f, 0.03f, -0.73f, -1.00f, -0.61f, 0.16f, 0.52f, 0.63f, 0.63f, 0.68f, 0.85f, 0.74f, 0.38f, -0.13f, -0.32f, -0.08f, 0.41f, 0.99f, 0.90f, 0.35f, 0.11f, -0.01f, -0.05f, -0.04f, -0.02f, 0.00f },
                new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 1.01f, 2.04f, 0.30f, -2.93f, -4.67f, -0.76f, 2.64f, 2.89f, 2.26f, 1.37f, -0.04f, -3.44f, -3.34f, -2.68f, -1.91f, -0.98f, 0.41f, 0.50f, -3.94f, -4.63f, -4.51f, -4.02f, -2.74f, -2.95f, -4.10f, -3.33f, -1.93f, -0.55f, 0.28f, -0.20f, -0.78f, -0.58f, -0.21f, 0.00f },
            },

            new[]
            {
                new[] { -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -2.95f, -5.91f, -5.97f, -5.38f, -3.66f, -2.23f, -1.02f, -0.08f, 1.38f, 3.47f, 4.53f, 3.79f, 3.39f, 3.53f, 3.82f, 4.14f, 4.38f, 4.52f, 4.26f, 3.13f, 0.59f, -3.65f, -7.38f, -8.91f, -8.55f, -5.88f, -2.40f, -1.68f, -2.29f, -2.17f, -1.57f, -0.88f, -0.29f, 0.00f },
                new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.14f, -0.24f, -0.05f, 0.39f, 0.94f, 1.30f, 1.30f, 1.12f, 0.70f, 0.03f, -0.60f, -0.81f, -0.50f, 0.13f, 0.43f, 0.51f, 0.52f, 0.56f, 0.70f, 0.61f, 0.31f, -0.10f, -0.26f, -0.07f, 0.34f, 0.81f, 0.74f, 0.28f, 0.09f, -0.01f, -0.04f, -0.03f, -0.01f, 0.00f },
                new[] { 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.83f, 1.67f, 0.25f, -2.40f, -3.82f, -0.62f, 2.16f, 2.36f, 1.85f, 1.12f, -0.04f, -2.81f, -2.73f, -2.19f, -1.57f, -0.81f, 0.34f, 0.41f, -3.22f, -3.79f, -3.69f, -3.29f, -2.24f, -2.41f, -3.35f, -2.72f, -1.58f, -0.45f, 0.23f, -0.16f, -0.64f, -0.48f, -0.17f, 0.00f },
            },

            new[]
            {
                new[] { -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -2.95f, -5.91f, -5.97f, -5.38f, -3.98f, -3.08f, -2.30f, -1.41f, 0.90f, 4.19f, 5.85f, 4.68f, 4.06f, 4.28f, 4.75f, 5.00f, 4.78f, 4.52f, 4.26f, 3.13f, 0.59f, -3.65f, -7.38f, -8.91f, -8.55f, -5.88f, -2.40f, -1.68f, -2.29f, -2.17f, -1.57f, -0.88f, -0.29f, 0.00f },
                new[] { -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.14f, -0.24f, -0.11f, 0.30f, 1.03f, 1.34f, 1.32f, 1.14f, 0.71f, 0.02f, -0.60f, -0.79f, -0.48f, 0.14f, 0.44f, 0.52f, 0.51f, 0.56f, 0.70f, 0.61f, 0.31f, -0.10f, -0.26f, -0.07f, 0.34f, 0.81f, 0.74f, 0.29f, 0.09f, -0.01f, -0.04f, -0.03f, -0.01f, 0.00f },
                new[] { -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, -0.00f, 0.83f, 1.67f, -1.03f, -4.64f, -0.83f, 1.30f, 2.32f, 2.35f, 1.84f, 1.13f, -0.04f, -2.82f, -2.73f, -2.19f, -1.56f, -0.80f, 0.34f, 0.41f, -3.22f, -3.79f, -3.69f, -3.29f, -2.24f, -2.41f, -3.35f, -2.72f, -1.58f, -0.45f, 0.23f, -0.16f, -0.64f, -0.48f, -0.17f, 0.00f },
            },
        };

        private static float _start = -100f;
        private static int _variant;
        private static float _scale = 1f;
        private static float _mirror = 1f;
        private static bool _pendingOff;

        private static float _raidStartAt = -1f;
        private const float RaidStartDelay = 1.0f;
        internal const float RaidStartBeep = 0.55f;

        internal static bool PendingOff
        {
            get { return _pendingOff; }
        }

        internal static void BindConfig()
        {
            CfgEnabled = ConfigMigration.Bind(
                Plugin.SecGeneral, "Toggle Animation", true,
                "A hand adjusts the camera when BodyCam is turned on or off.",
                null, 80, null, null);

            CfgStrength = ConfigMigration.Bind(
                Plugin.SecGeneral, "Toggle Animation Strength", 1.30f,
                "Strength of the camera jolt in that animation.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 79, null, null);

            CfgTiltUp = ConfigMigration.Bind(
                Plugin.SecGeneral, "Toggle Tilt Up", 0.00f,
                "How far the camera is lifted during that animation (degrees).",
                new AcceptableValueRange<float>(0.00f, 60.00f), 78, null, null);

            CfgHand = ConfigMigration.Bind(
                Plugin.SecGeneral, "Toggle Hand", true,
                "Left hand reaches to the camera in that animation (experimental).",
                null, 77, null, null);

            CfgRandom = ConfigMigration.Bind(
                Plugin.SecGeneral, "Random Readjust", true,
                "Now and then a hand adjusts the camera (not in a fight, not with busy hands).",
                null, 76, null, null);
        }

        internal static float Elapsed
        {
            get { return Active() ? Time.unscaledTime - _start : 100f; }
        }

        internal const float Duration = OffSwitchTime;

        private static bool Active()
        {
            return CfgEnabled != null && CfgEnabled.Value;
        }

        private static bool InRaid()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            return world != null && world.MainPlayer != null;
        }

        internal static bool HandleToggleKey()
        {
            if (_pendingOff)
            {
                _pendingOff = false;
                return true;
            }

            if (Plugin.Enabled.Value && Active() && InRaid())
            {
                Begin();
                _pendingOff = true;
                return true;
            }

            return false;
        }

        internal static void OnEnabledChanged(bool enabled)
        {
            _pendingOff = false;

            if (enabled && Active() && InRaid())
                Begin();
            else if (!enabled)
                _start = -100f;
        }

        internal static float ScheduleRaidStart()
        {
            _pendingOff = false;

            if (!Active())
            {
                _raidStartAt = -1f;
                return -1f;
            }

            _raidStartAt = Time.unscaledTime + RaidStartDelay;
            _lastCombat = -100f;
            _calmSince = -1f;
            _nextRandom = _raidStartAt + UnityEngine.Random.Range(RandomMin, RandomMax);
            return RaidStartDelay + RaidStartBeep;
        }

        internal static void Tick()
        {
            if (_raidStartAt >= 0f && Time.unscaledTime >= _raidStartAt)
            {
                _raidStartAt = -1f;

                if (Plugin.Enabled.Value && Active() && InRaid())
                    Begin();
            }

            TickRandom();

            if (!_pendingOff)
                return;

            if (!InRaid() || !Plugin.Enabled.Value ||
                Time.unscaledTime - _start >= OffSwitchTime)
            {
                _pendingOff = false;

                if (Plugin.Enabled.Value)
                {
                    Plugin.Enabled.Value = false;
                    Plugin.Log.LogInfo("[BodyCamera] DISABLED");
                }
            }
        }

        internal static void NoteCombat()
        {
            _lastCombat = Time.unscaledTime;
        }

        private static void TickRandom()
        {
            float now = Time.unscaledTime;

            if (CfgRandom == null || !CfgRandom.Value || !Active() || !Plugin.Enabled.Value || _pendingOff)
            {
                _calmSince = -1f;
                return;
            }

            Player player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null)
            {
                _calmSince = -1f;
                return;
            }

            if (_nextRandom < 0f)
                _nextRandom = now + UnityEngine.Random.Range(RandomMin, RandomMax);

            if (now < _nextRandom || !Calm(player, now))
            {
                _calmSince = -1f;
                return;
            }

            if (_calmSince < 0f)
                _calmSince = now;

            if (now - _calmSince < CalmNeeded)
                return;

            _calmSince = -1f;
            Begin();

            if (Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo("[BodyCamera] Random readjust.");
        }

        private static bool Calm(Player player, float now)
        {
            try
            {
                if (now - _start < OffSwitchTime + 1f || now - _lastCombat < CombatQuiet)
                    return false;

                if (BodyCamMenu.Open || Plugin.ThirdPerson)
                    return false;

                if (player.HealthController == null || !player.HealthController.IsAlive)
                    return false;

                if (SprintState.IsSprinting(player))
                    return false;

                var pwa = player.ProceduralWeaponAnimation;

                if (pwa != null && pwa.IsAiming)
                    return false;

                object hands = player.HandsController;

                if (hands == null)
                    return false;

                string name = hands.GetType().Name;

                if (name.Contains("Meds") || name.Contains("Grenade") || name.Contains("QuickUse"))
                    return false;

                return HandsIdle(hands);
            }
            catch
            {
                return false;
            }
        }

        private static bool HandsIdle(object hands)
        {
            Type t = hands.GetType();
            PropertyInfo p;

            if (!_idling.TryGetValue(t, out p))
            {
                p = null;

                for (Type x = t; x != null && p == null; x = x.BaseType)
                    p = x.GetProperty("CurrentOperation",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                _idling[t] = p;

                if (p == null && !_loggedIdling)
                {
                    _loggedIdling = true;
                    Plugin.Log.LogWarning("[BodyCamera] Random readjust: " + t.Name + ".CurrentOperation not found, waits.");
                }
            }

            if (p == null)
                return false;

            object op = p.GetValue(hands, null);
            return op != null && op.GetType().Name.EndsWith("Idling", StringComparison.Ordinal);
        }

        private static void Begin()
        {
            _start = Time.unscaledTime;
            _nextRandom = _start + UnityEngine.Random.Range(RandomMin, RandomMax);
            _variant = UnityEngine.Random.Range(0, Curves.Length);
            _scale = UnityEngine.Random.Range(0.85f, 1.15f);
            _mirror = UnityEngine.Random.value < 0.5f ? -1f : 1f;

            BodyCamBeep.PlayAdjust();
        }

        internal static Vector3 Evaluate()
        {
            if (CfgStrength == null || CfgTiltUp == null)
                return Vector3.zero;

            float t = Time.unscaledTime - _start;

            if (t < 0f || t >= OffSwitchTime)
                return Vector3.zero;

            float lift = Tilt(t) * Mathf.Max(0f, CfgTiltUp.Value);

            float pitchUp = lift;
            float yawRight = 0f;
            float rollRight = 0f;

            float k = Mathf.Max(0f, CfgStrength.Value) * _scale;

            float[][] c = Curves[Mathf.Clamp(_variant, 0, Curves.Length - 1)];
            int frames = c[0].Length;
            float x = t / FrameTime;

            if (k > 0.0001f && x < frames - 1)
            {
                int i = (int)x;
                float f = x - i;

                pitchUp += Mathf.Lerp(c[0][i], c[0][i + 1], f) * k;
                yawRight += Mathf.Lerp(c[1][i], c[1][i + 1], f) * _mirror * k;
                rollRight += Mathf.Lerp(c[2][i], c[2][i + 1], f) * _mirror * k;
            }

            return new Vector3(-pitchUp, yawRight, -rollRight);
        }

        private static float Tilt(float t)
        {
            if (t < 0.15f)
                return 0f;

            if (t < 0.55f)
                return Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.40f);

            if (t < 0.75f)
                return 1f;

            if (t < 1.35f)
                return Mathf.SmoothStep(1f, -0.08f, (t - 0.75f) / 0.60f);

            if (t < OffSwitchTime)
                return Mathf.SmoothStep(-0.08f, 0f, (t - 1.35f) / (OffSwitchTime - 1.35f));

            return 0f;
        }
    }
}

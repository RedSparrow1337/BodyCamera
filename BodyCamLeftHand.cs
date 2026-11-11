using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamLeftHand
    {
        private const float ScreenX = -0.72f;
        private const float ScreenY = -0.20f;

        private const float RefForward = 0.12f;
        private const float RefFov = 75f;
        private const float MinForward = 0.045f;
        private const float MaxForward = 0.20f;

        private static Camera _cam;
        private static Transform _camTransform;

        private const float ReachStart = 0.05f;
        private const float ReachEnd = 0.50f;
        private const float BackStart = 1.30f;

        private static Player _player;
        private static Transform _palm;
        private static Transform _elbow;
        private static Transform _upper;
        private static bool _failed;
        private static bool _logged;

        private static float Weight(float t)
        {
            float end = BodyCamAdjust.Duration;

            if (t <= ReachStart || t >= end)
                return 0f;

            if (t < ReachEnd)
                return Mathf.SmoothStep(0f, 1f, (t - ReachStart) / (ReachEnd - ReachStart));

            if (t < BackStart)
                return 1f;

            return Mathf.SmoothStep(1f, 0f, (t - BackStart) / (end - BackStart));
        }

        internal static void Apply(Transform cam)
        {
            if (cam == null || BodyCamAdjust.CfgHand == null || !BodyCamAdjust.CfgHand.Value)
                return;

            float w = Weight(BodyCamAdjust.Elapsed);

            if (w <= 0.001f)
                return;

            try
            {
                if (!FindArm())
                    return;

                Vector3 start = _palm.position;
                Vector3 local = TargetInCamera(cam);
                Vector3 goal =
                    cam.position +
                    cam.right * local.x +
                    cam.up * local.y +
                    cam.forward * local.z;

                Solve(Vector3.Lerp(start, goal, w), cam);
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogWarning("[BodyCamera] Toggle Hand off: " + ex.Message);
            }
        }

        private static Vector3 TargetInCamera(Transform cam)
        {
            if (cam != _camTransform)
            {
                _camTransform = cam;
                _cam = cam.GetComponent<Camera>();
            }

            float fov = _cam != null ? _cam.fieldOfView : RefFov;
            float aspect = _cam != null && _cam.aspect > 0.1f ? _cam.aspect : 16f / 9f;
            float near = _cam != null ? _cam.nearClipPlane : 0.01f;

            float tanV = Mathf.Tan(Mathf.Clamp(fov, 20f, 150f) * 0.5f * Mathf.Deg2Rad);
            float tanRef = Mathf.Tan(RefFov * 0.5f * Mathf.Deg2Rad);

            float z = RefForward * tanRef / tanV;
            z = Mathf.Clamp(z, Mathf.Max(MinForward, near + 0.03f), MaxForward);

            return new Vector3(
                z * tanV * aspect * ScreenX,
                z * tanV * ScreenY,
                z);
        }

        private static bool FindArm()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null)
                return false;

            if (player == _player)
                return !_failed;

            _player = player;
            _failed = true;
            _palm = player.PlayerBones != null ? player.PlayerBones.LeftPalm : null;
            _elbow = null;
            _upper = null;

            if (_palm == null)
            {
                Log("left palm not found");
                return false;
            }

            string chain = _palm.name;

            for (Transform t = _palm.parent; t != null; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                chain = t.name + " > " + chain;

                if (_upper == null && n.Contains("forearm"))
                {
                    _elbow = t;
                }
                else if (_elbow != null && n.Contains("upperarm"))
                {
                    _upper = t;
                    break;
                }

                if (n.Contains("spine") || n.Contains("pelvis"))
                    break;
            }

            if (_elbow == null || _upper == null)
            {
                Log("arm bones not found: " + chain);
                return false;
            }

            Log("arm " + _upper.name + " / " + _elbow.name + " / " + _palm.name);
            _failed = false;
            return true;
        }

        private static void Log(string text)
        {
            if (_logged)
                return;

            _logged = true;
            Plugin.Log.LogInfo("[BodyCamera] Toggle Hand: " + text);
        }

        private static void Solve(Vector3 target, Transform cam)
        {
            Vector3 a = _upper.position;
            Vector3 b = _elbow.position;
            Vector3 c = _palm.position;

            float lab = (b - a).magnitude;
            float lbc = (c - b).magnitude;
            Vector3 at = target - a;
            float dist = at.magnitude;

            if (lab < 0.01f || lbc < 0.01f || dist < 0.001f)
                return;

            Vector3 dir = at / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(lab - lbc) + 0.005f, lab + lbc - 0.005f);

            float x = (lab * lab - lbc * lbc + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));

            Vector3 pole = (b - a) - cam.up * 0.05f - cam.right * 0.05f;
            pole -= dir * Vector3.Dot(pole, dir);

            if (pole.sqrMagnitude < 1e-6f)
                pole = -cam.up;

            Vector3 elbow = a + dir * x + pole.normalized * h;

            _upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * _upper.rotation;

            Vector3 e = _elbow.position;
            Vector3 hand = a + dir * d;
            _elbow.rotation = Quaternion.FromToRotation(_palm.position - e, hand - e) * _elbow.rotation;
        }
    }
}

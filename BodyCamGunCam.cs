using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamGunCam
    {
        internal static ConfigEntry<KeyCode> CfgKey;
        internal static bool On;

        private const float MountBack = 0.02f;
        private const float MountRight = 0.07f;
        private const float SideMargin = 0.05f;
        private const float SideMax = 0.25f;
        private const float FrontLength = 0.30f;
        private const float MountUp = 0.03f;
        private const float MountTilt = 8f;
        private const float MountTurn = 3f;
        private const float EndInterval = 0.5f;
        private const float EndMin = -0.30f;
        private const float EndMax = 0.60f;
        private const float AxisLockDelay = 1f;

        private static int _activeFrame = -1;
        private static Camera _camera;
        private static Transform _fireport;
        private static Vector3 _localForward = Vector3.down;
        private static Quaternion _tarkovRotation = Quaternion.identity;

        private static object _weapon;
        private static float _weaponTime;
        private static Vector3 _lockedUp;
        private static bool _hasLockedUp;

        private static float _hipFov = -1f;

        private static Transform _weaponModel;
        private static float _endDist;
        private static float _frontRight;
        private static readonly List<Vector2> _pts = new List<Vector2>();
        private static float _nextEnd;
        private static bool _loggedEnd;
        private static readonly List<Renderer> _renderers = new List<Renderer>();
        private static readonly Vector3[] _corners = new Vector3[8];

        private static bool _moved;
        private static Vector3 _savedPosition;
        private static Quaternion _savedRotation;
        private static float _savedFov;

        private static bool _hooked;

        private static bool _lastActive;
        private static bool _hasLast;
        private static bool _failed;

        internal static bool Active
        {
            get { return _activeFrame == Time.frameCount && !_failed; }
        }

        internal static bool RecentlyActive
        {
            get { return Time.frameCount - _activeFrame <= 2 && !_failed; }
        }

        internal static Vector3 LastViewPosition;
        internal static Quaternion LastViewRotation = Quaternion.identity;

        internal static void BindConfig()
        {
            CfgKey = ConfigMigration.Bind(
                Plugin.SecGeneral, "Weapon Camera Key", KeyCode.F4,
                "Key that switches the camera on the end of the barrel, looking back at you.",
                null, 87, null, null);
        }

        internal static IEnumerator Loop()
        {
            if (!_hooked)
            {
                _hooked = true;
                Camera.onPreCull += OnPreCull;
            }

            var wait = new WaitForEndOfFrame();

            while (true)
            {
                yield return wait;

                try
                {
                    Restore();
                }
                catch (Exception ex)
                {
                    if (Time.frameCount % 600 == 0)
                        Plugin.Log.LogError("[BodyCamera] Weapon camera restore error: " + ex);
                }
            }
        }

        internal static void Toggle()
        {
            On = !On;
            Plugin.Log.LogInfo("[BodyCamera] Weapon camera " + (On ? "ON" : "OFF"));
        }

        internal static bool Update(Player player, Transform cameraTransform,
                                    Quaternion tarkovRotation, bool aiming)
        {
            if (!On || _failed || player == null || cameraTransform == null)
                return false;

            try
            {
                var fc = player.HandsController as Player.FirearmController;

                if (fc == null)
                {
                    _weapon = null;
                    return false;
                }

                BifacialTransform fp = fc.CurrentFireport;
                Transform fireport = fp != null ? fp.Original : null;

                if (fireport == null)
                    return false;

                if ((fireport.position - cameraTransform.position).sqrMagnitude > 9f)
                    return false;

                object weapon = fc.Item;

                if (!ReferenceEquals(weapon, _weapon))
                {
                    _weapon = weapon;
                    _weaponTime = Time.unscaledTime;
                    _hasLockedUp = false;
                    _endDist = 0f;
                    _frontRight = 0f;
                    _nextEnd = 0f;
                    _loggedEnd = false;
                }

                var pwa = player.ProceduralWeaponAnimation;
                _weaponModel = pwa != null && pwa.HandsContainer != null
                    ? (pwa.HandsContainer.Weapon != null ? pwa.HandsContainer.Weapon : pwa.HandsContainer.WeaponRoot)
                    : null;

                Vector3 local = player.LocalShotDirection;
                _localForward = local.sqrMagnitude > 0.0001f ? local.normalized : Vector3.down;

                _fireport = fireport;
                _tarkovRotation = tarkovRotation;

                if (_camera == null || _camera.transform != cameraTransform)
                    _camera = cameraTransform.GetComponent<Camera>();

                if (_camera == null)
                    return false;

                if (!aiming || _hipFov <= 0f)
                    _hipFov = _camera.fieldOfView;

                _activeFrame = Time.frameCount;
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogWarning("[BodyCamera] Weapon camera off after an error: " + ex);
                return false;
            }
        }

        internal static void Deactivate()
        {
            _activeFrame = -1;
            _hasLast = false;
            Restore();
        }

        internal static void NoteActive(bool active)
        {
            if (_hasLast && active != _lastActive)
                BodyCamOverlay.TriggerCam(active ? 2 : 1);

            _lastActive = active;
            _hasLast = true;
        }

        private static void OnPreCull(Camera cam)
        {
            if (!Active || _moved || cam == null || cam != _camera || _fireport == null)
                return;

            try
            {
                Vector3 forward = _fireport.TransformDirection(_localForward);

                if (forward.sqrMagnitude < 0.0001f)
                    return;

                forward.Normalize();

                Vector3 up = Vector3.ProjectOnPlane(TopOfGun(), forward);

                if (up.sqrMagnitude < 0.0001f)
                    up = Vector3.ProjectOnPlane(Vector3.up, forward);

                if (up.sqrMagnitude < 0.0001f)
                    return;

                up.Normalize();

                Vector3 right = Vector3.Cross(up, forward);

                if (Time.unscaledTime >= _nextEnd)
                {
                    _nextEnd = Time.unscaledTime + EndInterval;
                    MeasureEnd(forward, right);
                }

                float side = Mathf.Clamp(
                    _frontRight + Mathf.Max(SideMargin, cam.nearClipPlane * 1.5f),
                    MountRight, SideMax);

                Vector3 position =
                    _fireport.position +
                    forward * (_endDist - MountBack) +
                    right * side +
                    up * MountUp;

                Quaternion rotation =
                    Quaternion.LookRotation(-forward, up) *
                    Quaternion.Euler(-MountTilt, MountTurn, 0f);

                Transform t = cam.transform;

                _savedPosition = t.position;
                _savedRotation = t.rotation;
                _savedFov = cam.fieldOfView;
                _moved = true;

                t.SetPositionAndRotation(position, rotation);
                LastViewPosition = position;
                LastViewRotation = rotation;

                if (_hipFov > 0f)
                    cam.fieldOfView = _hipFov;
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogWarning("[BodyCamera] Weapon camera off after an error: " + ex);
            }
        }

        private static void MeasureEnd(Vector3 forward, Vector3 right)
        {
            if (_weaponModel == null)
            {
                _endDist = 0f;
                _frontRight = 0f;
                return;
            }

            _pts.Clear();

            _renderers.Clear();
            _weaponModel.GetComponentsInChildren(false, _renderers);

            Vector3 origin = _fireport.position;
            float best = float.NegativeInfinity;
            int used = 0;

            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];

                if (r == null || !r.enabled)
                    continue;

                Bounds b;
                Matrix4x4 m;

                var mr = r as MeshRenderer;
                var smr = r as SkinnedMeshRenderer;

                if (mr != null)
                {
                    var mf = r.GetComponent<MeshFilter>();

                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    b = mf.sharedMesh.bounds;
                    m = r.transform.localToWorldMatrix;
                }
                else if (smr != null)
                {
                    b = smr.localBounds;
                    Transform root = smr.rootBone != null ? smr.rootBone : smr.transform;
                    m = root.localToWorldMatrix;
                }
                else
                {
                    continue;
                }

                Vector3 c = b.center, e = b.extents;
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                int first = _pts.Count;

                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = new Vector3(
                        c.x + ((k & 1) != 0 ? e.x : -e.x),
                        c.y + ((k & 2) != 0 ? e.y : -e.y),
                        c.z + ((k & 4) != 0 ? e.z : -e.z));

                    Vector3 w = m.MultiplyPoint3x4(p) - origin;
                    float d = Vector3.Dot(w, forward);
                    _pts.Add(new Vector2(d, Vector3.Dot(w, right)));

                    if (d < lo) lo = d;
                    if (d > hi) hi = d;
                }

                if (hi - lo > 1.5f || hi > 1.5f)
                {
                    _pts.RemoveRange(first, _pts.Count - first);
                    continue;
                }

                used++;

                if (hi > best)
                    best = hi;
            }

            _renderers.Clear();

            _endDist = used > 0 ? Mathf.Clamp(best, EndMin, EndMax) : 0f;

            float wide = 0f;

            for (int i = 0; i < _pts.Count; i++)
            {
                if (_pts[i].x >= best - FrontLength && _pts[i].y > wide)
                    wide = _pts[i].y;
            }

            _pts.Clear();
            _frontRight = Mathf.Min(wide, SideMax);

            if (!_loggedEnd && Time.unscaledTime - _weaponTime >= AxisLockDelay)
            {
                _loggedEnd = true;

                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo(
                        "[BodyCamera] Weapon camera: front end of the gun " +
                        _endDist.ToString("0.000") + " m past the fireport, " +
                        _frontRight.ToString("0.000") + " m wide to the right (" + used + " meshes).");
            }
        }

        private static Vector3 TopOfGun()
        {
            Vector3 camUp = _tarkovRotation * Vector3.up;

            if (_hasLockedUp)
                return _fireport.TransformDirection(_lockedUp);

            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down };
            Vector3 best = Vector3.zero;
            float bestDot = -2f;

            for (int i = 0; i < axes.Length; i++)
            {
                if (Mathf.Abs(Vector3.Dot(axes[i], _localForward)) > 0.7f)
                    continue;

                float d = Vector3.Dot(_fireport.TransformDirection(axes[i]), camUp);

                if (d > bestDot)
                {
                    bestDot = d;
                    best = axes[i];
                }
            }

            if (Time.unscaledTime - _weaponTime >= AxisLockDelay)
            {
                _lockedUp = best;
                _hasLockedUp = true;

                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo("[BodyCamera] Weapon camera: top of the gun = fireport " + best + ".");
            }

            return _fireport.TransformDirection(best);
        }

        private static void Restore()
        {
            if (!_moved)
                return;

            _moved = false;

            if (_camera == null)
                return;

            _camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
            _camera.fieldOfView = _savedFov;
        }
    }
}

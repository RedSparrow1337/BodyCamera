using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using BepInEx.Configuration;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamRecoil
    {
        private static float _aimBlend;
        private static float _aimTarget;
        private static int _lastFrame = -1;

        private static float _holdBlend;

        private sealed class SwayProfile
        {
            public float MuzzleFallback;
        }

        private static readonly SwayProfile LongGun = new SwayProfile
        {
            MuzzleFallback = 0.75f
        };

        private static readonly SwayProfile PistolGun = new SwayProfile
        {
            MuzzleFallback = 0.5f
        };

        private static float _swaySpeed;
        private static float _vertSpeed;
        private static Vector3 _lastPlayerPos;
        private static bool _hasLastPlayerPos;

        private static SwayProfile CurrentProfile()
        {
            return _handsIsPistol ? PistolGun : LongGun;
        }

        internal static bool IsLocalAndActive(ProceduralWeaponAnimation pwa)
        {
            if (pwa == null || !Plugin.Enabled.Value)
                return false;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            return player != null && player.ProceduralWeaponAnimation == pwa;
        }

        internal static void Clear()
        {
            _inertiaAngle = Vector2.zero;
            _springInit = false;
        }

        private const float SpringFreq = 3.2f;
        private const float SpringDamping = 0.3f;
        private static readonly float[] _springX = new float[6];
        private static readonly float[] _springV = new float[6];
        private static bool _springInit;
        private static int _springFrame = -1;
        private static Vector3 _springPos, _springRot;

        private static void SpringMotion(ref Vector3 posCm, ref Vector3 rotDeg)
        {
            float k = Plugin.WeaponMotionSpring != null ? Mathf.Clamp01(Plugin.WeaponMotionSpring.Value) : 0f;

            if (k <= 0.0001f)
            {
                _springInit = false;
                return;
            }

            if (Time.frameCount != _springFrame)
            {
                _springFrame = Time.frameCount;
                float dt = Time.deltaTime;

                if (!_springInit || dt <= 0f || dt > 0.25f)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        _springX[i] = Ch(posCm, rotDeg, i);
                        _springV[i] = 0f;
                    }

                    _springInit = true;
                }
                else
                {
                    float w = 2f * Mathf.PI * SpringFreq;
                    const int sub = 8;
                    float h = dt / sub;

                    for (int i = 0; i < 6; i++)
                    {
                        float target = Ch(posCm, rotDeg, i);
                        float x = _springX[i], vel = _springV[i];

                        for (int s = 0; s < sub; s++)
                        {
                            float acc = w * w * (target - x) - 2f * SpringDamping * w * vel;
                            vel += acc * h;
                            x += vel * h;
                        }

                        _springX[i] = x;
                        _springV[i] = vel;
                    }
                }

                _springPos = new Vector3(_springX[0], _springX[1], _springX[2]);
                _springRot = new Vector3(_springX[3], _springX[4], _springX[5]);
            }

            posCm = Vector3.Lerp(posCm, _springPos, k);
            rotDeg = Vector3.Lerp(rotDeg, _springRot, k);
        }

        private static float Ch(Vector3 p, Vector3 r, int i)
        {
            switch (i)
            {
                case 0: return p.x;
                case 1: return p.y;
                case 2: return p.z;
                case 3: return r.x;
                case 4: return r.y;
                default: return r.z;
            }
        }

        private const float InertiaYawPerDeg = 0.6f;
        private const float InertiaPitchPerDeg = 0.4f;
        private const float InertiaRollPerDeg = 0.8f;
        private const float InertiaYawMax = 10f;
        private const float InertiaPitchMax = 6f;
        private const float InertiaRollMax = 12f;

        private const float InertiaSmoothTime = 0.1f;
        private const float InertiaMuzzleFallback = 0.7f;

        private static Vector2 _inertiaAngle;

        private static Weapon _handsWeapon;
        private static bool _handsIsPistol;

        internal static bool IsPistolInHands(Player player)
        {
            var fc = player != null ? player.HandsController as Player.FirearmController : null;
            Weapon w = fc != null ? fc.Weapon : null;

            if (w == null)
                return false;

            if (w != _handsWeapon)
            {
                _handsWeapon = w;

                try
                {
                    _handsIsPistol = DetectType(w) == RecoilType.Pistol;
                }
                catch (System.Exception)
                {
                    _handsIsPistol = false;
                }
            }

            return _handsIsPistol;
        }

        internal enum RecoilType
        {
            Pistol,
            Shotgun,
            Rifle,
            AssaultRifle,
            Smg
        }

        private static readonly System.Collections.Generic.Dictionary<string, RecoilType> TypeCache =
            new System.Collections.Generic.Dictionary<string, RecoilType>();

        private static RecoilType DetectType(Weapon weapon)
        {
            string key = weapon.TemplateId.ToString();
            RecoilType type;

            if (TypeCache.TryGetValue(key, out type))
                return type;

            string wc = (weapon.WeapClass ?? "").ToLowerInvariant();
            string caliber = weapon.AmmoCaliber ?? "";

            if (wc == "pistol" || wc == "revolver")
                type = RecoilType.Pistol;
            else if (wc == "shotgun" || wc == "grenadelauncher" ||
                     caliber.StartsWith("Caliber12g") || caliber.StartsWith("Caliber20g") ||
                     caliber.StartsWith("Caliber23x75"))
                type = RecoilType.Shotgun;
            else if (wc == "smg")
                type = RecoilType.Smg;
            else if (wc == "marksmanrifle" || wc == "sniperrifle" || weapon.BoltAction)
                type = RecoilType.Rifle;
            else
                type = RecoilType.AssaultRifle;

            TypeCache[key] = type;

            Plugin.Log.LogInfo(
                "[BodyCamera] Weapon type " + type + " for weapon " + key +
                " (class '" + weapon.WeapClass + "', caliber '" + caliber + "').");

            return type;
        }

        private static FieldInfo _fireportField;
        private static PropertyInfo _fireportProp;
        private static FieldInfo _bifacialOriginal;
        private static bool _fireportLookup;

        private static void TickInertia(float dt)
        {
            Vector2 target = Vector2.zero;

            if (Plugin.HasLead)
            {
                Vector3 f = Plugin.LeadLocal * Vector3.forward;

                if (!float.IsNaN(f.x) && f.z > 0.01f)
                {
                    float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;

                    target = new Vector2(
                        SoftLimit(yaw * InertiaYawPerDeg, InertiaYawMax),
                        SoftLimit(pitch * InertiaPitchPerDeg, InertiaPitchMax));
                }
            }

            _inertiaAngle = Vector2.Lerp(
                _inertiaAngle,
                target,
                1f - Mathf.Exp(-dt / InertiaSmoothTime));

            if (float.IsNaN(_inertiaAngle.x) || float.IsNaN(_inertiaAngle.y))
                _inertiaAngle = Vector2.zero;
        }

        private static PropertyInfo _bonesProp;
        private static FieldInfo _palmField;
        private static FieldInfo _palmOriginal;
        private static bool _palmLookup;
        private static int _palmFrame = -1;
        private static Transform _palmCached;

        private static Transform FindRightPalm()
        {
            int frame = Time.frameCount;

            if (frame == _palmFrame)
                return _palmCached;

            _palmFrame = frame;
            _palmCached = null;

            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player == null)
                    return null;

                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                if (!_palmLookup)
                {
                    _palmLookup = true;
                    _bonesProp = typeof(Player).GetProperty("PlayerBones", all);

                    if (_bonesProp != null)
                    {
                        _palmField = _bonesProp.PropertyType.GetField("RightPalm", all);

                        if (_palmField != null && !typeof(Transform).IsAssignableFrom(_palmField.FieldType))
                            _palmOriginal = _palmField.FieldType.GetField("Original", all);
                    }

                    if (_palmField == null)
                        Plugin.Log.LogWarning("[BodyCamera] Right palm not found, Wrist Kick turns around the weapon root.");
                }

                if (_bonesProp == null || _palmField == null)
                    return null;

                object bones = _bonesProp.GetValue(player, null);

                if (bones == null)
                    return null;

                object palm = _palmField.GetValue(bones);

                if (palm == null)
                    return null;

                _palmCached = _palmOriginal != null
                    ? _palmOriginal.GetValue(palm) as Transform
                    : palm as Transform;
            }
            catch (System.Exception)
            {
                _palmCached = null;
            }

            return _palmCached;
        }

        private static int _muzzleFrame = -1;
        private static Transform _muzzleCached;

        private static Transform FindMuzzle()
        {
            int frame = Time.frameCount;

            if (frame != _muzzleFrame)
            {
                _muzzleFrame = frame;
                _muzzleCached = LookupMuzzle();
            }

            return _muzzleCached;
        }

        private static Transform LookupMuzzle()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null)
                return null;

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            if (!_fireportLookup)
            {
                _fireportLookup = true;
                _fireportProp = typeof(Player).GetProperty("Fireport", all);

                if (_fireportProp == null)
                    _fireportField = typeof(Player).GetField("Fireport", all);

                System.Type t = _fireportProp != null ? _fireportProp.PropertyType
                    : _fireportField != null ? _fireportField.FieldType : null;

                if (t != null)
                    _bifacialOriginal = t.GetField("Original", all);

                if (_bifacialOriginal == null)
                    Plugin.Log.LogWarning("[BodyCamera] Fireport not found, weapon inertia uses a fixed pivot.");
            }

            if (_bifacialOriginal == null)
                return null;

            object fp = _fireportProp != null
                ? _fireportProp.GetValue(player, null)
                : _fireportField.GetValue(player);

            return fp != null ? _bifacialOriginal.GetValue(fp) as Transform : null;
        }

        private static Vector3 MuzzlePivot(Transform v, SwayProfile profile)
        {
            float fallback = profile != null ? profile.MuzzleFallback : InertiaMuzzleFallback;

            Transform muzzle = FindMuzzle();
            Vector3 pivot = muzzle != null
                ? muzzle.position
                : v.position + v.forward * fallback;

            if ((pivot - v.position).sqrMagnitude > 4f)
                pivot = v.position + v.forward * fallback;

            return pivot;
        }

        private static void ApplyInertia(Transform root, Transform v)
        {
            float k = _handsIsPistol
                ? Mathf.Lerp(
                    Mathf.Max(0f, Plugin.WeaponInertiaPistolHip.Value),
                    Mathf.Max(0f, Plugin.WeaponInertiaPistolAds.Value),
                    _aimBlend)
                : Mathf.Lerp(
                    Mathf.Max(0f, Plugin.WeaponInertiaHip.Value),
                    Mathf.Max(0f, Plugin.WeaponInertiaAds.Value),
                    _aimBlend);

            Vector2 a = _inertiaAngle * k;

            if (a.sqrMagnitude < 0.0001f)
                return;

            float roll = SoftLimit(a.x * InertiaRollPerDeg, InertiaRollMax);

            Vector3 pivot = MuzzlePivot(v, null);

            Quaternion r =
                Quaternion.AngleAxis(a.x, v.up) *
                Quaternion.AngleAxis(-a.y, v.right) *
                Quaternion.AngleAxis(roll, v.forward);

            root.SetPositionAndRotation(
                pivot + r * (root.position - pivot),
                r * root.rotation);
        }

        private static void ApplyWeaponDeadZone(Transform root, Transform v)
        {
            Vector2 o = Plugin.WeaponDeadOffset;

            if (o.sqrMagnitude < 0.0001f || float.IsNaN(o.x) || float.IsNaN(o.y))
                return;

            Transform palm = FindRightPalm();
            Vector3 pivot = palm != null ? palm.position : root.position;

            if ((pivot - v.position).sqrMagnitude > 4f)
                pivot = root.position;

            Quaternion r =
                Quaternion.AngleAxis(o.x, v.up) *
                Quaternion.AngleAxis(-o.y, v.right);

            root.SetPositionAndRotation(
                pivot + r * (root.position - pivot),
                r * root.rotation);
        }

        private static float SoftLimit(float x, float limit)
        {
            float e = Mathf.Exp(-2f * x / limit);
            return limit * (1f - e) / (1f + e);
        }

        private static void Tick(ProceduralWeaponAnimation pwa)
        {
            int frame = Time.frameCount;

            if (frame == _lastFrame)
                return;

            _lastFrame = frame;

            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);

            TickInertia(dt);

            float aimTarget = pwa.IsAiming ? 1f : 0f;
            _aimTarget = aimTarget;
            _aimBlend = Mathf.MoveTowards(_aimBlend, aimTarget, dt * 5f);

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            float rawSpeed = 0f;
            float verticalSpeed = 0f;
            bool holdingBreath = false;

            if (player != null)
            {
                Vector3 pos = player.Transform.position;

                if (_hasLastPlayerPos)
                {
                    Vector3 d = pos - _lastPlayerPos;
                    verticalSpeed = Mathf.Abs(d.y) / dt;
                    d.y = 0f;
                    rawSpeed = Mathf.Min(d.magnitude / dt, 8f);
                }

                _lastPlayerPos = pos;
                _hasLastPlayerPos = true;

                IsPistolInHands(player);

                try
                {
                    holdingBreath =
                        player.Physical != null && player.Physical.HoldingBreath;
                }
                catch (System.Exception)
                {
                    holdingBreath = false;
                }
            }

            _holdBlend = Mathf.MoveTowards(
                _holdBlend, holdingBreath ? 1f : 0f, dt * 4f);

            _swaySpeed = Mathf.Lerp(_swaySpeed, rawSpeed, 1f - Mathf.Exp(-8f * dt));
            _vertSpeed = Mathf.Lerp(_vertSpeed, verticalSpeed, 1f - Mathf.Exp(-6f * dt));

            bool airborne = verticalSpeed > Mathf.Max(2.5f, 1.2f * _swaySpeed);

            BodyCamGunMotion.Tick(dt, _swaySpeed, airborne, _aimBlend,
                player != null && SprintState.IsSprinting(player));
        }

        private static bool _hasDelta;

        private static bool _hasRecoilDelta;
        private static Quaternion _recoilDeltaRot = Quaternion.identity;
        private static Vector3 _recoilDeltaPos;

        internal static Vector3 RemoveRecoilShift(Vector3 p)
        {
            if (!_hasRecoilDelta)
                return p;

            return Quaternion.Inverse(_recoilDeltaRot) * (p - _recoilDeltaPos);
        }
        private static Transform _deltaRoot;
        private static Quaternion _deltaRot = Quaternion.identity;
        private static Vector3 _deltaPos;
        private static bool _overlapRestored;
        private static Vector3 _savedPos;
        private static Quaternion _savedRot;

        internal static void BeginOverlapCheck(Player.FirearmController controller)
        {
            _overlapRestored = false;

            if (!_hasDelta || _deltaRoot == null)
                return;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null || player.HandsController != controller)
                return;

            _savedPos = _deltaRoot.position;
            _savedRot = _deltaRoot.rotation;

            Quaternion inverse = Quaternion.Inverse(_deltaRot);

            _deltaRoot.SetPositionAndRotation(
                inverse * (_savedPos - _deltaPos),
                inverse * _savedRot);

            _overlapRestored = true;
        }

        internal static void EndOverlapCheck()
        {
            if (!_overlapRestored)
                return;

            _overlapRestored = false;

            if (_deltaRoot != null)
                _deltaRoot.SetPositionAndRotation(_savedPos, _savedRot);
        }

        internal static void ClearDelta()
        {
            _hasDelta = false;
            _hasRecoilDelta = false;
            _deltaRoot = null;
        }

        internal static void ResetForRaid()
        {
            Clear();
            ClearDelta();
            BodyCamRecoilProbe.Flush();
            BodyCamMagCheck.Reset();
            _overlapRestored = false;
            _hasLastPlayerPos = false;
            BodyCamGunMotion.Reset();
            _swaySpeed = 0f;
            _vertSpeed = 0f;
            _muzzleFrame = -1;
            _muzzleCached = null;
            _palmFrame = -1;
            _palmCached = null;
            _handsWeapon = null;
            _aimBlend = 0f;
            _aimTarget = 0f;
            _cornerHip = 1f;
            _cornerHipVel = 0f;
            _holdBlend = 0f;
            _lastFrame = -1;
            _cornerTarget = 0f;
            _cornerAlpha = 0f;
            _cornerGoal = 0f;
            _cornerVel = 0f;
            _lastCornerFrame = -1;
            _raiseTarget = 0f;
            _raise = 0f;
            _raiseVel = 0f;
            _lastRaiseShot = -100f;
            ResetShove();
        }

        internal static void AfterWeaponPose(ProceduralWeaponAnimation pwa)
        {
            if (!IsLocalAndActive(pwa))
            {
                var mine = Singleton<GameWorld>.Instance?.MainPlayer;

                if (mine != null && mine.ProceduralWeaponAnimation == pwa)
                    ClearDelta();

                return;
            }

            _hasDelta = false;
            _hasRecoilDelta = false;

            BodyCamRecoilProbe.Sample(pwa);

            Tick(pwa);

            if (pwa.HandsContainer == null)
                return;

            Transform root = pwa.HandsContainer.WeaponRootAnim;

            if (root == null)
                return;

            Camera view = Camera.main;

            if (view == null)
                return;

            Transform v = view.transform;

            Vector3 cleanPos = root.position;
            Quaternion cleanRot = root.rotation;

            ApplyOffsets(root, v);

            _deltaRot = root.rotation * Quaternion.Inverse(cleanRot);
            _deltaPos = root.position - _deltaRot * cleanPos;
            _deltaRoot = root;
            _hasDelta = true;
        }

        private static void ApplyGunMotion(Transform root, Transform v)
        {
            float idleK = Mathf.Lerp(
                Mathf.Max(0f, Plugin.IdleMotionStrength.Value),
                Mathf.Max(0f, Plugin.AdsIdleMotionStrength.Value),
                _aimBlend) * (1f - _holdBlend);

            float walkK = Mathf.Lerp(
                Mathf.Max(0f, Plugin.WalkMotionStrength.Value),
                Mathf.Max(0f, Plugin.AdsWalkMotionStrength.Value),
                _aimBlend);

            float notThird = 1f - Mathf.Clamp01(Plugin.ThirdWeight);
            idleK *= notThird;
            walkK *= notThird;

            if (idleK <= 0.0001f && walkK <= 0.0001f)
            {
                _springInit = false;
                return;
            }

            Vector3 posCm;
            Vector3 rotDeg;
            BodyCamGunMotion.Evaluate(_handsIsPistol, idleK, walkK, out posCm, out rotDeg);

            SpringMotion(ref posCm, ref rotDeg);

            if (posCm.sqrMagnitude < 1e-8f && rotDeg.sqrMagnitude < 1e-8f)
                return;

            Transform palm = FindRightPalm();
            Vector3 pivot = palm != null && (palm.position - v.position).sqrMagnitude < 1f
                ? palm.position
                : root.position;

            float angle = rotDeg.magnitude;
            Quaternion local = angle > 0.00001f
                ? Quaternion.AngleAxis(angle, rotDeg / angle)
                : Quaternion.identity;

            Quaternion r = v.rotation * local * Quaternion.Inverse(v.rotation);

            Vector3 move =
                (v.right * posCm.x + v.up * posCm.y + v.forward * posCm.z) * 0.01f;

            root.SetPositionAndRotation(
                pivot + r * (root.position - pivot) + move,
                r * root.rotation);
        }

        private const float CornerGoalEase = 5f;
        private const float CornerSmoothIn = 0.35f;
        private const float CornerSmoothOut = 0.5f;

        private const float CornerAimFade = 0.45f;
        private const int CornerLanes = 5;
        private const float CornerLaneStep = 0.10f;
        private const float CornerReachWalk = 1.50f;
        private const float CornerReachRun = 1.00f;
        private const float CornerRunSpeed = 2.0f;

        private static float _cornerTarget;
        private static float _cornerAlpha;
        private static float _cornerGoal;
        private static float _cornerVel;
        private static float _cornerHip = 1f;
        private static float _cornerHipVel;
        private static int _cornerHitsDbg;
        private static int _lastCornerFrame = -1;
        private static float _cornerNextLog;
        private static int _cornerMask;
        private static bool _cornerMaskLookup;
        private static readonly RaycastHit[] _cornerHits = new RaycastHit[8];

        private static int CornerMask()
        {
            if (_cornerMaskLookup)
                return _cornerMask;

            _cornerMaskLookup = true;
            _cornerMask = Physics.DefaultRaycastLayers;

            try
            {
                var type = typeof(Player).Assembly.GetType("LayersMaskController");
                var field = type != null
                    ? type.GetField("HighPolyWithTerrainMask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                object value = field != null ? field.GetValue(null) : null;

                if (value is LayerMask)
                    _cornerMask = ((LayerMask)value).value;
                else if (value is int)
                    _cornerMask = (int)value;
            }
            catch
            {
            }

            return _cornerMask;
        }

        private static bool CornerBlocked(Vector3 from, Vector3 dir, float reach, float radius, Transform self)
        {
            int n = radius > 0f
                ? Physics.SphereCastNonAlloc(from, radius, dir, _cornerHits, reach, CornerMask(), QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(from, dir, _cornerHits, reach, CornerMask(), QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Collider col = _cornerHits[i].collider;

                if (col == null || (self != null && col.transform.IsChildOf(self)))
                    continue;

                return true;
            }

            return false;
        }

        private static void TickCorner(float dt, bool on)
        {
            int hits = 0;
            Transform self = null;
            Vector3 basePos = Vector3.zero, right = Vector3.zero;
            bool probed = false;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (on && player != null && !SprintState.IsSprinting(player))
            {
                Vector3 look = player.LookDirection;

                if (look.sqrMagnitude > 0.0001f)
                {
                    look.Normalize();
                    self = player.GetComponent<Transform>();

                    float eye = Mathf.Lerp(0.9f, 1.6f, Mathf.Clamp01(player.PoseLevel));
                    basePos = self.position + Vector3.up * eye;

                    right = self.right;
                    right.y = 0f;
                    right.Normalize();

                    float reach = _swaySpeed > CornerRunSpeed ? CornerReachRun : CornerReachWalk;

                    for (int i = 1; i <= CornerLanes; i++)
                    {
                        if (CornerBlocked(basePos + right * (i * CornerLaneStep), look, reach, 0f, self)) hits++;
                        if (CornerBlocked(basePos - right * (i * CornerLaneStep), look, reach, 0f, self)) hits--;
                    }

                    probed = true;
                }
            }

            _cornerHitsDbg = hits;

            if (hits != 0)
                _cornerTarget = Mathf.Clamp(-hits / (float)CornerLanes, -1f, 1f);
            else
                _cornerTarget += (0f - _cornerTarget) * Mathf.Clamp01(dt * 4f);

            float goal = on ? _cornerTarget : 0f;

            if (probed && Mathf.Abs(goal) > 0.001f &&
                CornerBlocked(basePos, right * Mathf.Sign(goal), 0.25f, 0.20f, self))
                goal = 0f;

            float tiltSpeed = Plugin.CornerTiltSpeed != null ? Mathf.Clamp(Plugin.CornerTiltSpeed.Value, 0.5f, 3f) : 1f;
            _cornerGoal += (goal - _cornerGoal) * (1f - Mathf.Exp(-CornerGoalEase * tiltSpeed * dt));

            float smooth = (Mathf.Abs(_cornerGoal) > Mathf.Abs(_cornerAlpha) ? CornerSmoothIn : CornerSmoothOut) / tiltSpeed;
            _cornerAlpha = Mathf.SmoothDamp(_cornerAlpha, _cornerGoal, ref _cornerVel, smooth, Mathf.Infinity, dt);

            if (Mathf.Abs(_cornerAlpha) < 0.001f && Mathf.Abs(_cornerGoal) < 0.001f)
            {
                _cornerAlpha = 0f;
                _cornerVel = 0f;
            }
        }

        private static void ApplyCornerTilt(Transform root, Transform v)
        {
            float maxDeg = Plugin.CornerTilt != null ? Mathf.Max(0f, Plugin.CornerTilt.Value) : 0f;
            float hip = 1f - _aimBlend;

            if (_lastCornerFrame != Time.frameCount)
            {
                _lastCornerFrame = Time.frameCount;
                float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);
                TickCorner(dt, maxDeg > 0.001f && hip > 0.001f);

                float hipGoal = _aimTarget > 0.5f ? 0f : 1f;
                _cornerHip = Mathf.SmoothDamp(_cornerHip, hipGoal, ref _cornerHipVel, CornerAimFade, Mathf.Infinity, dt);
                if (Mathf.Abs(_cornerHip - hipGoal) < 0.001f)
                {
                    _cornerHip = hipGoal;
                    _cornerHipVel = 0f;
                }

                if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value &&
                    (_cornerHitsDbg != 0 || Mathf.Abs(_cornerAlpha) > 0.01f) && Time.unscaledTime >= _cornerNextLog)
                {
                    _cornerNextLog = Time.unscaledTime + 2f;
                    Plugin.Log.LogInfo(
                        "[BodyCamera] Corner tilt: lanes " + _cornerHitsDbg + ", value " + _cornerAlpha.ToString("0.00") +
                        ", speed " + _swaySpeed.ToString("0.0") + " m/s.");
                }
            }

            float speedK = Mathf.Lerp(2f, 1.5f, Mathf.InverseLerp(1.2f, 2.0f, _swaySpeed));
            float deg = _cornerAlpha * maxDeg * speedK * _cornerHip;

            if (Mathf.Abs(deg) < 0.01f)
                return;

            Transform palm = FindRightPalm();
            Vector3 pivot = palm != null && (palm.position - v.position).sqrMagnitude < 1f
                ? palm.position
                : root.position;

            Quaternion r = Quaternion.AngleAxis(-deg, v.forward);

            root.SetPositionAndRotation(
                pivot + r * (root.position - pivot),
                r * root.rotation);
        }

        private const float RaiseMaxCm = 8f;
        private const float RaisePerShot = 0.15f;
        private const float RaiseHold = 0.15f;
        private const float RaiseDownTime = 0.5f;
        private const float RaiseUpSmooth = 0.06f;
        private static float _raiseTarget;
        private static float _raise;
        private static float _raiseVel;
        private static float _lastRaiseShot = -100f;
        private static int _raiseFrame = -1;

        internal static void OnShot()
        {
            if (Plugin.FireRaise == null || Plugin.FireRaise.Value <= 0.001f)
                return;

            if (_aimTarget > 0.5f || Plugin.ThirdWeight > 0.5f || !_handsIsPistol)
                return;

            _raiseTarget = Mathf.Min(1f, _raiseTarget + RaisePerShot);
            _lastRaiseShot = Time.time;
        }

        private static void ApplyFireRaise(Transform root, Transform v)
        {
            float k = Plugin.FireRaise != null ? Mathf.Max(0f, Plugin.FireRaise.Value) : 0f;

            if (_raiseFrame != Time.frameCount)
            {
                _raiseFrame = Time.frameCount;
                float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);

                bool off = k <= 0.001f || _aimTarget > 0.5f || Plugin.ThirdWeight > 0.5f || !_handsIsPistol;

                if (off || Time.time - _lastRaiseShot > RaiseHold)
                    _raiseTarget = Mathf.MoveTowards(_raiseTarget, 0f, dt / RaiseDownTime);

                if (_raiseTarget > _raise)
                    _raise = Mathf.SmoothDamp(_raise, _raiseTarget, ref _raiseVel, RaiseUpSmooth, Mathf.Infinity, dt);
                else
                {
                    _raise = _raiseTarget;
                    _raiseVel = 0f;
                }
            }

            float lift = Mathf.SmoothStep(0f, 1f, _raise) * k * RaiseMaxCm * 0.01f *
                         (1f - _aimBlend) * (1f - Mathf.Clamp01(Plugin.ThirdWeight));

            if (lift < 0.0001f)
                return;

            root.position += v.up * lift;
        }

        private const float ShoveRange = 1.5f;
        private const float ShoveRadius = 0.15f;
        private const float ShoveRearmRange = 1.8f;
        private const float ShoveOutTime = 0.10f;
        private const float ShoveBackTime = 0.25f;
        private const float ShoveForwardCm = 18f;
        private const float ShoveUpCm = 10f;
        private const float ShoveYawDeg = 15f;
        private const float ShoveRollDeg = 25f;
        private const float PushDistance = 1f;
        private const float PushTime = 0.3f;
        private static Player _pushTarget;
        private static Vector3 _pushDir;
        private static float _pushStart = -100f;
        private static float _pushDone;
        private const float ShoveDamage = 1f;
        private static float _shoveStart = -100f;
        private static int _shoveFrame = -1;
        private static bool _shoveDamagePending;
        private static Player _shoveTarget;
        private static BodyPartCollider _shovePart;
        private static Collider _shoveCollider;
        private static Vector3 _shovePoint;
        private static Vector3 _shoveNormal;
        private static Vector3 _shoveDir;
        private static readonly System.Collections.Generic.HashSet<string> _shoveDone =
            new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.List<string> _shoveDrop =
            new System.Collections.Generic.List<string>();
        private static readonly RaycastHit[] _shoveHits = new RaycastHit[24];
        private static readonly RaycastHit[] _shoveLos = new RaycastHit[16];
        private static int _shoveMask;
        private static bool _shoveMaskLookup;
        private static bool _shoveFailed;

        private static int ShoveMask()
        {
            if (_shoveMaskLookup)
                return _shoveMask;

            _shoveMaskLookup = true;
            _shoveMask = Physics.DefaultRaycastLayers;

            try
            {
                var type = typeof(Player).Assembly.GetType("LayersMaskController");
                var field = type != null
                    ? type.GetField("HitColliderMask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                object value = field != null ? field.GetValue(null) : null;

                if (value is LayerMask)
                    _shoveMask = ((LayerMask)value).value;
                else if (value is int)
                    _shoveMask = (int)value;
            }
            catch
            {
            }

            return _shoveMask;
        }

        private static void ResetShove()
        {
            _shoveStart = -100f;
            _shoveFrame = -1;
            _shoveDamagePending = false;
            _shoveTarget = null;
            _shovePart = null;
            _shoveCollider = null;
            _shoveDone.Clear();
            _shoveFailed = false;
            _pushTarget = null;
            _pushStart = -100f;
            _pushDone = 0f;
        }

        private static bool ShoveBusy(float now)
        {
            return now - _shoveStart < ShoveOutTime + ShoveBackTime;
        }

        private static void TickShove()
        {
            if (_shoveFailed)
                return;

            try
            {
                GameWorld gw = Singleton<GameWorld>.Instance;
                Player player = gw != null ? gw.MainPlayer : null;
                float now = Time.time;

                if (_shoveDamagePending && now - _shoveStart >= ShoveOutTime)
                {
                    _shoveDamagePending = false;
                    ShoveHit(gw, player);
                }

                TickPush(gw, now);

                bool on = Plugin.MuzzleShove != null && Plugin.MuzzleShove.Value &&
                          player != null &&
                          _aimTarget < 0.5f && Plugin.ThirdWeight < 0.5f &&
                          player.HandsController is Player.FirearmController;

                if (!on)
                {
                    _shoveDone.Clear();
                    return;
                }

                Transform self = player.GetComponent<Transform>();
                float eye = Mathf.Lerp(0.9f, 1.6f, Mathf.Clamp01(player.PoseLevel));
                Vector3 from = self.position + Vector3.up * eye;

                if (_shoveDone.Count > 0)
                {
                    _shoveDrop.Clear();

                    foreach (string id in _shoveDone)
                    {
                        Player p = gw.GetAlivePlayerByProfileID(id);

                        if (p == null ||
                            (p.GetComponent<Transform>().position + Vector3.up * eye - from).magnitude > ShoveRearmRange)
                            _shoveDrop.Add(id);
                    }

                    for (int i = 0; i < _shoveDrop.Count; i++)
                        _shoveDone.Remove(_shoveDrop[i]);
                }

                if (ShoveBusy(now))
                    return;

                Vector3 look = player.LookDirection;

                if (look.sqrMagnitude < 0.0001f)
                    return;

                look.Normalize();

                int n = Physics.SphereCastNonAlloc(from, ShoveRadius, look, _shoveHits, ShoveRange,
                    ShoveMask(), QueryTriggerInteraction.Ignore);

                float best = float.MaxValue;
                Player target = null;
                BodyPartCollider part = null;
                Collider col = null;
                Vector3 point = Vector3.zero, normal = -look;

                for (int i = 0; i < n; i++)
                {
                    Collider c = _shoveHits[i].collider;

                    if (c == null || c.transform.IsChildOf(self))
                        continue;

                    BodyPartCollider b = c.GetComponent<BodyPartCollider>();

                    if (b == null)
                        continue;

                    string id = b.PlayerProfileID;

                    if (string.IsNullOrEmpty(id) && b.Player != null)
                        id = b.Player.ProfileId;

                    if (string.IsNullOrEmpty(id) || _shoveDone.Contains(id))
                        continue;

                    Player p = gw.GetAlivePlayerByProfileID(id);

                    if (p == null || ReferenceEquals(p, player) || !p.IsAI)
                        continue;

                    float d = _shoveHits[i].distance;
                    Vector3 pt = d > 0f ? _shoveHits[i].point : c.ClosestPoint(from);

                    if (d >= best || !ShoveClear(from, pt, self, p))
                        continue;

                    best = d;
                    target = p;
                    part = b;
                    col = c;
                    point = pt;
                    normal = d > 0f ? _shoveHits[i].normal : -look;
                }

                if (target == null)
                    return;

                _shoveDone.Add(target.ProfileId);
                _shoveStart = now;
                _shoveDamagePending = true;
                _shoveTarget = target;
                _shovePart = part;
                _shoveCollider = col;
                _shovePoint = point;
                _shoveNormal = normal;
                _shoveDir = look;
            }
            catch (System.Exception ex)
            {
                _shoveFailed = true;
                Plugin.Log.LogWarning("[BodyCamera] Muzzle shove off after an error: " + ex.Message);
            }
        }

        private static bool ShoveClear(Vector3 from, Vector3 to, Transform self, Player target)
        {
            Vector3 d = to - from;
            float len = d.magnitude;

            if (len < 0.05f)
                return true;

            Transform other = target.GetComponent<Transform>();
            int n = Physics.RaycastNonAlloc(from, d / len, _shoveLos, len - 0.03f, CornerMask(), QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Collider c = _shoveLos[i].collider;

                if (c == null || c.transform.IsChildOf(self) ||
                    (other != null && c.transform.IsChildOf(other)) ||
                    c.GetComponent<BodyPartCollider>() != null)
                    continue;

                return false;
            }

            return true;
        }

        private static void ShoveHit(GameWorld gw, Player player)
        {
            Player target = _shoveTarget;
            BodyPartCollider part = _shovePart;
            _shoveTarget = null;
            _shovePart = null;

            if (gw == null || player == null || target == null || part == null || _shoveCollider == null)
                return;

            if (gw.GetAlivePlayerByProfileID(target.ProfileId) == null)
                return;

            var fc = player.HandsController as Player.FirearmController;

            EFT.Ballistics.DamageInfo info = default(EFT.Ballistics.DamageInfo);
            info.DamageType = EDamageType.Melee;
            info.Damage = ShoveDamage;
            info.PenetrationPower = 0f;
            info.ArmorDamage = 0f;
            info.Direction = _shoveDir;
            info.HitCollider = _shoveCollider;
            info.HitPoint = _shovePoint;
            info.HitNormal = _shoveNormal;
            info.Player = gw.GetEverExistedBridgeByProfileID(player.ProfileId);
            info.HittedBallisticCollider = part;
            info.Weapon = fc != null ? fc.Item : null;
            info.IsForwardHit = true;

            gw.HackShot(info);

            try
            {
                float yaw = (Random.value < 0.5f ? -1f : 1f) * Random.Range(6f, 10f);
                target.Rotation = target.Rotation + new Vector2(yaw, 0f);
            }
            catch
            {
            }

            Vector3 flat = _shoveDir;
            flat.y = 0f;

            if (flat.sqrMagnitude > 0.0001f)
            {
                _pushTarget = target;
                _pushDir = flat.normalized;
                _pushStart = Time.time;
                _pushDone = 0f;
            }

            BodyCamAdjust.NoteCombat();

            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo("[BodyCamera] Muzzle shove: " + part.BodyPartType + ", " +
                                   (_shovePoint - player.GetComponent<Transform>().position).magnitude.ToString("0.00") + " m.");
        }

        private static void TickPush(GameWorld gw, float now)
        {
            if (_pushTarget == null)
                return;

            float u = Mathf.Clamp01((now - _pushStart) / PushTime);
            float goal = PushDistance * (1f - (1f - u) * (1f - u));
            float step = goal - _pushDone;

            if (gw == null || gw.GetAlivePlayerByProfileID(_pushTarget.ProfileId) == null)
                u = 1f;
            else if (step > 0.0001f)
            {
                try
                {
                    float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);
                    _pushTarget.MovementContext.DirectApplyMotion(_pushDir * step, dt);
                }
                catch (System.Exception ex)
                {
                    u = 1f;
                    Plugin.Log.LogWarning("[BodyCamera] Muzzle shove push failed: " + ex.Message);
                }

                _pushDone = goal;
            }

            if (u >= 1f)
                _pushTarget = null;
        }

        private static void ApplyMuzzleShove(Transform root, Transform v)
        {
            if (_shoveFrame != Time.frameCount)
            {
                _shoveFrame = Time.frameCount;
                TickShove();
            }

            float t = Time.time - _shoveStart;

            if (t < 0f || t >= ShoveOutTime + ShoveBackTime)
                return;

            float s;

            if (t < ShoveOutTime)
                s = Mathf.Sin(t / ShoveOutTime * Mathf.PI * 0.5f);
            else
            {
                float u = (t - ShoveOutTime) / ShoveBackTime;
                s = 1f - u * u * (3f - 2f * u);
            }

            s *= (1f - _aimBlend) * (1f - Mathf.Clamp01(Plugin.ThirdWeight));

            if (s < 0.0001f)
                return;

            Vector3 move = (v.forward * ShoveForwardCm + v.up * ShoveUpCm) * 0.01f * s;

            Transform palm = FindRightPalm();
            Vector3 pivot = (palm != null && (palm.position - v.position).sqrMagnitude < 1f
                ? palm.position
                : root.position) + move;

            root.position += move;

            Quaternion r = Quaternion.AngleAxis(-ShoveYawDeg * s, v.up) *
                           Quaternion.AngleAxis(ShoveRollDeg * s, v.forward);

            root.SetPositionAndRotation(
                pivot + r * (root.position - pivot),
                r * root.rotation);
        }

        private static void ApplyOffsets(Transform root, Transform v)
        {
            ApplyWeaponDeadZone(root, v);

            ApplyInertia(root, v);

            Vector3 beforePos = root.position;
            Quaternion beforeRot = root.rotation;

            ApplyGunMotion(root, v);

            ApplyCornerTilt(root, v);

            ApplyFireRaise(root, v);

            ApplyMuzzleShove(root, v);

            _recoilDeltaRot = root.rotation * Quaternion.Inverse(beforeRot);
            _recoilDeltaPos = root.position - _recoilDeltaRot * beforePos;
            _hasRecoilDelta = true;
        }
    }

    internal sealed class BodyCamOverlapPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod(
                "WeaponOverlapping",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance)
        {
            try
            {
                BodyCamRecoil.BeginOverlapCheck(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[BodyCamera] Overlap isolation error: " + ex);
            }
        }

        [PatchPostfix]
        private static void Postfix()
        {
            try
            {
                BodyCamRecoil.EndOverlapCheck();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[BodyCamera] Overlap isolation error: " + ex);
            }
        }
    }

    internal sealed class BodyCamWeaponKickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ProceduralWeaponAnimation).GetMethod(
                "ApplyComplexRotation",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            try
            {
                BodyCamRecoil.AfterWeaponPose(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[BodyCamera] Weapon kick error: " + ex);
            }
        }
    }
}

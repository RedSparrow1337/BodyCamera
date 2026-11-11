using System;
using System.Reflection;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamMagCheck
    {
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgLook;

        private const float LookIn = 0.3f;
        private const float LookOut = 0.4f;
        private const float LookMaxDeg = 60f;
        private static float _look;
        private static Vector2 _lookAngles;

        private const int CheckMagazineType = 3;
        private const float FadeIn = 10f;
        private const float FadeOut = 6f;
        private const float HideTarkovAfter = 1.5f;

        private static FieldInfo _typeField;
        private static bool _typeLookup;

        private static int _lastFrame = -1;
        private static bool _checking;

        private static float _requestedAt = -100f;
        private static bool _sawOperation;
        private const float MaxCheckTime = 6f;
        private const float StartGrace = 0.4f;
        private const float FallbackTime = 2.5f;
        private static int _logged;
        private static int _errors;
        private static float _endedAt = -100f;
        private static float _alpha;

        private static Weapon _weapon;
        private static Transform _mag;
        private static Renderer[] _renderers;
        private static Transform _palm;

        private static int _count = -1;
        private static int _max = -1;
        internal static string Text = "";
        internal static string Ammo = "";
        internal static bool IsEmpty;
        internal static bool IsLow;

        private static int _shown = -1;

        private static string _panelAmmo = "";
        private static int _panelLogged;

        internal static Rect MagRect;
        internal static bool HasRect;

        internal static float Alpha { get { return _alpha; } }

        internal static bool Checking { get { return _checking; } }

        internal static void BindConfig()
        {
            CfgEnabled = ConfigMigration.Bind(
                Plugin.SecOverlay, "Show Mag Check Count", true,
                "Exact rounds left next to the magazine when you check it.",
                null, 58, null, null);

            CfgLook = ConfigMigration.Bind(
                Plugin.SecWeapon, "Mag Check Look", 0.50f,
                "On a magazine check the camera turns to the top of the magazine (0 = off, 1 = centred).",
                new AcceptableValueRange<float>(0f, 1f), 74, null, null);
        }

        private static bool LookOn
        {
            get { return CfgLook != null && CfgLook.Value > 0.001f; }
        }

        internal static Vector3 LookOffset(Quaternion camRot, Vector3 camPos)
        {
            TickState();

            float k = CfgLook != null ? Mathf.Clamp01(CfgLook.Value) : 0f;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            bool on = _checking && k > 0.001f && _mag != null;

            _look = Mathf.MoveTowards(_look, on ? 1f : 0f, dt / (on ? LookIn : LookOut));

            if (on)
            {
                Vector3 aim;

                if (!MagTop(camRot * Vector3.up, out aim))
                    aim = _mag.position;

                Vector3 local = Quaternion.Inverse(camRot) * (aim - camPos);

                if (local.z > 0.02f)
                {
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                    var goal = new Vector2(
                        Mathf.Clamp(pitch, -LookMaxDeg, LookMaxDeg),
                        Mathf.Clamp(yaw, -LookMaxDeg, LookMaxDeg));

                    _lookAngles = _look <= dt / LookIn + 0.0001f
                        ? goal
                        : Vector2.Lerp(_lookAngles, goal, 1f - Mathf.Exp(-dt / 0.08f));
                }
            }

            if (_look <= 0f)
            {
                _lookAngles = Vector2.zero;
                return Vector3.zero;
            }

            float w = Mathf.SmoothStep(0f, 1f, _look) * k;
            return new Vector3(_lookAngles.x * w, _lookAngles.y * w, 0f);
        }

        internal static bool HideTarkovPanel
        {
            get
            {
                if (CfgEnabled == null || !CfgEnabled.Value || !Plugin.Enabled.Value)
                    return false;

                float now = Time.unscaledTime;
                return _checking || now - _requestedAt < StartGrace || now - _endedAt < HideTarkovAfter;
            }
        }

        internal static void OnCheckAmmoRequested()
        {
            _requestedAt = Time.unscaledTime;
            _sawOperation = false;
            _panelAmmo = "";
        }

        internal static void OnPanelText(string first, string second)
        {
            if (_panelLogged < 3)
            {
                _panelLogged++;
                Plugin.Log.LogInfo("[BodyCamera] Mag check: Tarkov panel '" + first + "' / '" + second + "'.");
            }

            string ammo = StripTags(second);

            if (ammo.Length > 0)
                _panelAmmo = ammo;
        }

        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "").Trim();
        }

        internal static void OnCheckAmmoResult(bool started)
        {
            if (!started)
                _requestedAt = -100f;
        }

        internal static void Reset()
        {
            _requestedAt = -100f;
            _sawOperation = false;
            _checking = false;
            _alpha = 0f;
            _weapon = null;
            _mag = null;
            _renderers = null;
            _palm = null;
            _count = -1;
            _max = -1;
            Text = "";
            Ammo = "";
            IsEmpty = false;
            IsLow = false;
            _shown = -1;
            _panelAmmo = "";
            HasRect = false;
            _endedAt = -100f;
        }

        private static bool IsCheckingMagazine(Player.FirearmController fc)
        {
            var op = fc.CurrentOperation as Player.FirearmController.UtilityOperation;

            if (op == null)
                return false;

            if (!_typeLookup)
            {
                _typeLookup = true;
                _typeField = typeof(Player.FirearmController.UtilityOperation).GetField(
                    "_utilityOperationType",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_typeField == null)
                    Plugin.Log.LogWarning("[BodyCamera] Mag check: utility type not found, the count is not shown.");
            }

            if (_typeField == null)
                return false;

            object v = _typeField.GetValue(op);
            return v != null && Convert.ToInt32(v) == CheckMagazineType;
        }

        internal static void Tick()
        {
            TickState();

            if (_alpha > 0f)
                UpdateRect();
            else
                HasRect = false;
        }

        private static void TickState()
        {
            int frame = Time.frameCount;

            if (frame == _lastFrame)
                return;

            _lastFrame = frame;

            bool checking = false;
            Player.FirearmController fc = null;

            try
            {
                if (((CfgEnabled != null && CfgEnabled.Value) || LookOn) && Plugin.Enabled.Value)
                {
                    var player = Singleton<GameWorld>.Instance?.MainPlayer;
                    fc = player != null ? player.HandsController as Player.FirearmController : null;

                    float since = Time.unscaledTime - _requestedAt;
                    bool op = fc != null && IsCheckingMagazine(fc);

                    if (op)
                        _sawOperation = true;

                    bool requested = fc != null && since < MaxCheckTime &&
                                     (_sawOperation ? op : since < FallbackTime);

                    if (fc != null && (op || requested))
                    {
                        checking = true;

                        if (!_checking || _weapon != fc.Weapon)
                        {
                            StartCheck(player, fc);

                            if (_logged < 3)
                            {
                                _logged++;
                                Plugin.Log.LogInfo(
                                    "[BodyCamera] Mag check: operation " +
                                    (fc.CurrentOperation != null ? fc.CurrentOperation.GetType().Name : "none") +
                                    ", utility check " + op +
                                    ", magazine model " + (_mag != null ? _mag.name : "not found") +
                                    ", renderers " + (_renderers != null ? _renderers.Length : 0) + ".");
                            }
                        }

                        UpdateCount(fc);
                    }
                }
            }
            catch (Exception ex)
            {
                checking = false;

                if (_errors++ < 3)
                    Plugin.Log.LogWarning("[BodyCamera] Mag check error: " + ex);
            }

            if (_checking && !checking)
                _endedAt = Time.unscaledTime;

            _checking = checking;

            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            _alpha = checking && Text.Length > 0 && CfgEnabled != null && CfgEnabled.Value
                ? Mathf.MoveTowards(_alpha, 1f, dt * FadeIn)
                : Mathf.MoveTowards(_alpha, 0f, dt * FadeOut);
        }

        private static void StartCheck(Player player, Player.FirearmController fc)
        {
            _weapon = fc.Weapon;
            _shown = -1;
            _mag = null;
            _renderers = null;
            _palm = player.PlayerBones != null ? player.PlayerBones.LeftPalm : null;

            GameObject go = fc.ControllerGameObject;

            if (go != null)
                _mag = FindByName(go.transform, "mod_magazine");

            if (_mag != null)
                _renderers = _mag.GetComponentsInChildren<Renderer>(false);
        }

        private static Transform FindByName(Transform root, string name)
        {
            var queue = new System.Collections.Generic.Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform t = queue.Dequeue();

                if (t.name == name)
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    queue.Enqueue(t.GetChild(i));
            }

            return null;
        }

        private static void UpdateCount(Player.FirearmController fc)
        {
            Weapon w = fc.Weapon;
            Magazine mag = w != null ? w.GetCurrentMagazine() : null;

            if (mag == null)
            {
                Text = "";
                Ammo = "";
                IsEmpty = false;
                IsLow = false;
                _count = _max = -1;
                return;
            }

            Ammo = "";

            int count = mag.Count;
            int max = mag.MaxCount;

            int shown = count;

            if (shown != _shown || count != _count || max != _max || Text.Length == 0)
            {
                _count = count;
                _max = max;
                _shown = shown;
                Text = shown + " / " + max;
            }

            IsEmpty = count <= 0;
            IsLow = !IsEmpty && count * 2 < max;
        }

        private const float MagHalfWidth = 0.025f;

        private static readonly System.Collections.Generic.List<Vector3> _pts =
            new System.Collections.Generic.List<Vector3>();
        private static int _ptsFrame = -1;
        private const float TopBand = 0.01f;
        private const float MaxSpread = 0.6f;

        private static void MagPoints()
        {
            if (_ptsFrame == Time.frameCount)
                return;

            _ptsFrame = Time.frameCount;
            _pts.Clear();

            if (_renderers == null)
                return;

            for (int r = 0; r < _renderers.Length; r++)
            {
                Renderer rend = _renderers[r];

                if (rend == null || !rend.enabled || !rend.gameObject.activeInHierarchy)
                    continue;

                Bounds b;
                Matrix4x4 m;
                var smr = rend as SkinnedMeshRenderer;

                if (smr != null)
                {
                    b = smr.localBounds;
                    m = (smr.rootBone != null ? smr.rootBone : smr.transform).localToWorldMatrix;
                }
                else
                {
                    var mf = rend.GetComponent<MeshFilter>();

                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    b = mf.sharedMesh.bounds;
                    m = rend.transform.localToWorldMatrix;
                }

                Vector3 c = b.center, e = b.extents;

                for (int i = 0; i < 8; i++)
                {
                    _pts.Add(m.MultiplyPoint3x4(c + new Vector3(
                        (i & 1) != 0 ? e.x : -e.x,
                        (i & 2) != 0 ? e.y : -e.y,
                        (i & 4) != 0 ? e.z : -e.z)));
                }
            }

            if (_pts.Count > 0)
            {
                Bounds all = new Bounds(_pts[0], Vector3.zero);

                for (int i = 1; i < _pts.Count; i++)
                    all.Encapsulate(_pts[i]);

                if (all.size.magnitude > MaxSpread)
                    _pts.Clear();
            }
        }

        private static bool MagTop(Vector3 up, out Vector3 top)
        {
            top = Vector3.zero;
            MagPoints();

            if (_pts.Count == 0)
                return false;

            float best = float.NegativeInfinity;

            for (int i = 0; i < _pts.Count; i++)
                best = Mathf.Max(best, Vector3.Dot(_pts[i], up));

            float sum = 0f;

            for (int i = 0; i < _pts.Count; i++)
            {
                float wgt = 1f - (best - Vector3.Dot(_pts[i], up)) / TopBand;

                if (wgt > 0f)
                {
                    top += _pts[i] * wgt;
                    sum += wgt;
                }
            }

            if (sum <= 0f)
                return false;

            top /= sum;
            return true;
        }

        private static void UpdateRect()
        {
            HasRect = false;
            Camera cam = Camera.main;

            if (cam == null)
                return;

            Vector3 topWorld;
            Vector2 ts;

            if (_mag != null && MagTop(cam.transform.up, out topWorld) && ToScreen(cam, topWorld, out ts))
            {
                Vector2 side;

                if (ToScreen(cam, topWorld + cam.transform.right * MagHalfWidth, out side))
                {
                    float sideHalf = Mathf.Clamp(Mathf.Abs(side.x - ts.x), 4f, Screen.width * 0.2f);
                    SetRect(new Rect(ts.x - sideHalf, ts.y, sideHalf * 2f, 1f));
                    return;
                }
            }

            Transform t = _mag != null ? _mag : _palm;

            if (t == null)
                return;

            Vector3 p = t.position;
            Vector2 a, b;

            if (!ToScreen(cam, p, out a) || !ToScreen(cam, p + cam.transform.right * MagHalfWidth, out b))
                return;

            float half = Mathf.Clamp(Mathf.Abs(b.x - a.x), 8f, Screen.height * 0.08f);

            SetRect(new Rect(a.x - half, a.y - half * 0.5f, half * 2f, half * 2f));
        }

        private const float RectSmooth = 0.03f;
        private static int _rectFrame = -1;
        private static Rect _rectPrev;
        private static bool _rectFresh;

        private static void SetRect(Rect r)
        {
            int frame = Time.frameCount;

            if (_rectFrame != frame)
            {
                _rectFresh = _rectFrame < frame - 2;
                _rectPrev = MagRect;
                _rectFrame = frame;
            }

            bool jump = Mathf.Abs(r.x - _rectPrev.x) > Screen.height * 0.15f ||
                        Mathf.Abs(r.y - _rectPrev.y) > Screen.height * 0.15f;

            if (_rectFresh || jump)
            {
                MagRect = r;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Max(Time.unscaledDeltaTime, 0.0001f) / RectSmooth);
                MagRect = new Rect(
                    Mathf.Lerp(_rectPrev.x, r.x, k),
                    Mathf.Lerp(_rectPrev.y, r.y, k),
                    Mathf.Lerp(_rectPrev.width, r.width, k),
                    Mathf.Lerp(_rectPrev.height, r.height, k));
            }

            HasRect = true;
        }

        private static bool ToScreen(Camera cam, Vector3 world, out Vector2 screen)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            screen = new Vector2(v.x * Screen.width, (1f - v.y) * Screen.height);
            return v.z > 0.01f;
        }

        private static bool AddPoint(Camera cam, Vector3 world, ref float xMin, ref float yMin, ref float xMax, ref float yMax)
        {
            Vector3 v = cam.WorldToViewportPoint(world);

            if (v.z <= 0.01f)
                return false;

            float x = v.x * Screen.width;
            float y = (1f - v.y) * Screen.height;

            if (x < xMin) xMin = x;
            if (y < yMin) yMin = y;
            if (x > xMax) xMax = x;
            if (y > yMax) yMax = y;

            return true;
        }
    }

    internal sealed class BodyCamCheckAmmoPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.FirearmController).GetMethod(
                "CheckAmmo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance)
        {
            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player != null && ReferenceEquals(player.HandsController, __instance))
                    BodyCamMagCheck.OnCheckAmmoRequested();
            }
            catch
            {
            }
        }

        [PatchPostfix]
        private static void Postfix(Player.FirearmController __instance, bool __result)
        {
            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player != null && ReferenceEquals(player.HandsController, __instance))
                    BodyCamMagCheck.OnCheckAmmoResult(__result);
            }
            catch
            {
            }
        }
    }

    internal sealed class BodyCamAmmoPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.UI.AmmoCountPanel).GetMethod(
                "Show",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
        }

        [PatchPrefix]
        private static bool Prefix(string __0, string __1)
        {
            try
            {
                BodyCamMagCheck.OnPanelText(__0, __1);
                BodyCamMagCheck.Tick();
                return !BodyCamMagCheck.HideTarkovPanel;
            }
            catch
            {
                return true;
            }
        }
    }
}

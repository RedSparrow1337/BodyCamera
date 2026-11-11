using System;
using System.Reflection;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamAimDot
    {
        private const float MaxDistance = 500f;
        private const float FadeTime = 0.1f;

        private static readonly RaycastHit[] _hits = new RaycastHit[24];
        private static int _mask;
        private static bool _maskLookup;

        private static Type _handsType;
        private static PropertyInfo _dirProp;
        private static PropertyInfo _fireportProp;
        private static FieldInfo _originalField;

        private static bool _want;
        private static bool _hasPoint;
        private static Vector2 _gui;
        private static float _alpha;
        private static Texture2D _tex;
        private static bool _failed;

        internal static void Update(Player player, Transform self, Transform cam, bool show)
        {
            _want = false;

            if (_failed || !show || player == null || cam == null)
                return;

            try
            {
                Vector3 origin, dir;

                if (!Muzzle(player, out origin, out dir))
                    return;

                Vector3 point = origin + dir * MaxDistance;
                int n = Physics.RaycastNonAlloc(origin, dir, _hits, MaxDistance, Mask(), QueryTriggerInteraction.Ignore);
                float best = MaxDistance;

                for (int i = 0; i < n; i++)
                {
                    Collider c = _hits[i].collider;

                    if (c == null || (self != null && c.transform.IsChildOf(self)))
                        continue;

                    if (_hits[i].distance < best)
                    {
                        best = _hits[i].distance;
                        point = _hits[i].point;
                    }
                }

                Camera camera = cam.GetComponent<Camera>();

                if (camera == null)
                    return;

                Vector3 v = camera.WorldToViewportPoint(point);

                if (v.z <= 0.01f)
                    return;

                _gui = new Vector2(v.x * Screen.width, (1f - v.y) * Screen.height);
                _hasPoint = true;
                _want = true;
            }
            catch (Exception ex)
            {
                _failed = true;
                Plugin.Log.LogWarning("[BodyCamera] Aim dot off after an error: " + ex.Message);
            }
        }

        internal static void Reset()
        {
            _want = false;
            _alpha = 0f;
            _hasPoint = false;
            _failed = false;
        }

        internal static void Draw()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            _alpha = Mathf.MoveTowards(_alpha, _want ? 1f : 0f, Time.unscaledDeltaTime / FadeTime);

            if (_alpha <= 0.001f || !_hasPoint)
                return;

            EnsureTexture();

            Vector2 p = BodyCamLens.PictureToOverlay(_gui);
            float s = Mathf.Max(0.5f, Screen.height / 1080f);
            float size = Mathf.Round(6f * s);
            Rect r = new Rect(Mathf.Round(p.x - size * 0.5f), Mathf.Round(p.y - size * 0.5f), size, size);

            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.9f * _alpha);
            GUI.DrawTexture(r, _tex);
            GUI.color = old;
        }

        private static void EnsureTexture()
        {
            if (_tex != null)
                return;

            const int n = 32;
            _tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;
            _tex.hideFlags = HideFlags.HideAndDontSave;

            float c = (n - 1) * 0.5f;

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (n * 0.5f);
                    float outer = Mathf.Clamp01((1f - d) * 12f);
                    float inner = Mathf.Clamp01((0.6f - d) * 12f);
                    Color col = Color.Lerp(new Color(0f, 0f, 0f, 0.75f * outer), Color.white, inner);
                    _tex.SetPixel(x, y, col);
                }
            }

            _tex.Apply();
        }

        private static bool Muzzle(Player player, out Vector3 origin, out Vector3 dir)
        {
            origin = Vector3.zero;
            dir = Vector3.forward;

            object hands = player.HandsController;

            if (hands == null)
                return false;

            Type t = hands.GetType();

            if (t != _handsType)
            {
                _handsType = t;
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _dirProp = Find(t, "WeaponDirection", all);
                _fireportProp = Find(t, "CurrentFireport", all);
                _originalField = _fireportProp != null ? _fireportProp.PropertyType.GetField("Original", all) : null;
            }

            if (_dirProp == null || _fireportProp == null || _originalField == null)
                return false;

            object fp = _fireportProp.GetValue(hands, null);
            Transform muzzle = fp != null ? _originalField.GetValue(fp) as Transform : null;

            if (muzzle == null)
                return false;

            dir = (Vector3)_dirProp.GetValue(hands, null);

            if (dir.sqrMagnitude < 0.0001f)
                return false;

            dir.Normalize();
            origin = muzzle.position;
            return true;
        }

        private static PropertyInfo Find(Type t, string name, BindingFlags flags)
        {
            for (Type x = t; x != null; x = x.BaseType)
            {
                PropertyInfo p = x.GetProperty(name, flags | BindingFlags.DeclaredOnly);

                if (p != null)
                    return p;
            }

            return null;
        }

        private static int Mask()
        {
            if (_maskLookup)
                return _mask;

            _maskLookup = true;
            _mask = Get("HighPolyWithTerrainMask") | Get("HitColliderMask");

            if (_mask == 0)
                _mask = Physics.DefaultRaycastLayers;

            return _mask;
        }

        private static int Get(string field)
        {
            try
            {
                Type type = typeof(Player).Assembly.GetType("LayersMaskController");
                FieldInfo f = type != null
                    ? type.GetField(field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                object value = f != null ? f.GetValue(null) : null;

                if (value is LayerMask)
                    return ((LayerMask)value).value;

                if (value is int)
                    return (int)value;
            }
            catch
            {
            }

            return 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal enum MenuColor
    {
        Red,
        Yellow,
        Green
    }

    internal static class BodyCamMenu
    {
        internal static ConfigEntry<KeyCode> CfgMenuKey;
        internal static ConfigEntry<string> CfgLanguage;
        internal static ConfigEntry<float> CfgPosX;
        internal static ConfigEntry<float> CfgPosY;
        internal static ConfigEntry<MenuColor> CfgColor;

        internal static readonly ConfigEntry<KeyCode>[] CfgProfileKeys = new ConfigEntry<KeyCode>[ProfileCount];

        private static readonly Dictionary<string, ConfigEntryBase> _entries =
            new Dictionary<string, ConfigEntryBase>();

        private static readonly Dictionary<string, List<string>> _sectionKeys =
            new Dictionary<string, List<string>>();

        private static readonly string[] Sections =
        {
            Plugin.SecGeneral, Plugin.SecPosition, Plugin.SecRotation, Plugin.SecMotion,
            Plugin.SecWeapon, Plugin.SecOverlay, Plugin.SecLens, Plugin.SecSounds,
            Plugin.SecBody, Plugin.SecDebug
        };

        private static readonly string[] Hidden = { "Menu Language", "Menu Position X", "Menu Position Y" };

        private static ConfigFile _config;
        private static int _tab;
        private static ConfigEntryBase _capture;
        private static bool _dirty;
        private static bool _saveOnSet = true;
        private static bool _holdInput;
        private static float _holdUntil;
        private static Behaviour _eventSystem;
        private static string _version;

        private static bool _drag;
        private static Vector2 _dragOffset;
        private static float _scroll;
        private static float _contentH;

        internal static Rect PanelPixels;

        internal static bool Open { get; private set; }

        internal static bool BlockGameInput
        {
            get { return Open || _holdInput; }
        }

        internal static bool EatsKeys
        {
            get { return Open && (_capture != null || GUIUtility.keyboardControl != 0); }
        }

        private static int Lang
        {
            get { return CfgLanguage != null && CfgLanguage.Value == "RU" ? 0 : 1; }
        }

        internal static void BindConfig(ConfigFile config)
        {
            _config = config;

            CfgMenuKey = ConfigMigration.Bind(
                Plugin.SecGeneral, "Menu Key", KeyCode.F8,
                "Opens and closes the BodyCamera menu.",
                null, 87, null, null);

            CfgColor = ConfigMigration.Bind(
                Plugin.SecGeneral, "Menu Color", MenuColor.Red,
                "Accent colour of the BodyCamera menu.",
                null, 86, null, null);

            CfgLanguage = ConfigMigration.Bind(
                Plugin.SecGeneral, "Menu Language", "EN",
                "Menu language (switched in the menu header).",
                new AcceptableValueList<string>("EN", "RU"), 3, null, null);

            CfgPosX = ConfigMigration.Bind(
                Plugin.SecGeneral, "Menu Position X", 0.5f,
                "Menu position on the screen (drag the menu by its header).",
                new AcceptableValueRange<float>(0f, 1f), 2, null, null);

            CfgPosY = ConfigMigration.Bind(
                Plugin.SecGeneral, "Menu Position Y", 0.5f,
                "Menu position on the screen (drag the menu by its header).",
                new AcceptableValueRange<float>(0f, 1f), 1, null, null);

            for (int i = 0; i < ProfileCount; i++)
            {
                CfgProfileKeys[i] = ConfigMigration.Bind(
                    Plugin.SecGeneral, ProfileKeyName(i + 1), KeyCode.None,
                    "Key that loads profile " + (i + 1) + ".",
                    null, 0, null, null);
            }
        }

        private static string ProfileKeyName(int slot)
        {
            return "Profile " + slot + " Key";
        }

        internal static void Register(string section, string key, int order, ConfigEntryBase entry)
        {
            if (entry == null || Array.IndexOf(Hidden, key) >= 0)
                return;

            _entries[key] = entry;

            if (key.StartsWith("Profile ", StringComparison.Ordinal) &&
                key.EndsWith(" Key", StringComparison.Ordinal))
                return;

            List<string> list;

            if (!_sectionKeys.TryGetValue(section, out list))
            {
                list = new List<string>();
                _sectionKeys[section] = list;
            }

            list.Add(key);
        }

        internal static void Tick()
        {
            if (_holdInput && !Open && (!Input.anyKey || Time.unscaledTime > _holdUntil))
                _holdInput = false;

            if (_capture == null && CfgMenuKey != null && CfgMenuKey.Value != KeyCode.None &&
                Input.GetKeyDown(CfgMenuKey.Value))
            {
                if (Open)
                    Hide();
                else
                    Show();

                return;
            }

            if (_capture == null && GUIUtility.keyboardControl == 0)
            {
                for (int i = 0; i < ProfileCount; i++)
                {
                    var k = CfgProfileKeys[i];

                    if (k != null && k.Value != KeyCode.None && Input.GetKeyDown(k.Value))
                    {
                        LoadProfileByKey(i + 1);
                        break;
                    }
                }
            }

            if (!Open)
                return;

            if (_capture == null && Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            if (_drag && !Input.GetMouseButton(0))
                _drag = false;

            KeepCursor();
        }

        private static void Show()
        {
            Open = true;
            _holdInput = true;
            _capture = null;
            _drag = false;
            GUIUtility.keyboardControl = 0;

            if (_config != null)
            {
                _saveOnSet = _config.SaveOnConfigSet;
                _config.SaveOnConfigSet = false;
            }

            SetEventSystem(false);
            KeepCursor();
        }

        private static void Hide()
        {
            Open = false;
            _capture = null;
            _drag = false;
            GUIUtility.keyboardControl = 0;

            _holdInput = true;
            _holdUntil = Time.unscaledTime + 2f;

            if (_config != null)
            {
                _config.SaveOnConfigSet = _saveOnSet;

                if (_dirty)
                {
                    try
                    {
                        _config.Save();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("[BodyCamera] Could not save settings: " + ex.Message);
                    }
                }
            }

            _dirty = false;
            SetEventSystem(true);
        }

        private static void KeepCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static void SetEventSystem(bool on)
        {
            try
            {
                if (!on)
                {
                    _eventSystem = null;
                    Type t = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
                    PropertyInfo p = t != null ? t.GetProperty("current", BindingFlags.Static | BindingFlags.Public) : null;
                    var es = p != null ? p.GetValue(null, null) as Behaviour : null;

                    if (es != null && es.enabled)
                    {
                        es.enabled = false;
                        _eventSystem = es;
                    }
                }
                else if (_eventSystem != null)
                {
                    _eventSystem.enabled = true;
                    _eventSystem = null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Menu: EventSystem switch failed: " + ex.Message);
            }
        }

        private static void Set(ConfigEntryBase entry, object value)
        {
            entry.BoxedValue = value;
            _dirty = true;
        }

        private const float W = 560f;
        private const float H = 470f;
        private const float HeaderH = 38f;
        private const float FooterH = 44f;
        private const float TabsW = 162f;
        private const float Radius = 12f;
        private const float RowH = 30f;
        private const float SliderRowH = 46f;

        private static Color Red { get { return Theme().Accent; } }
        private static Color RedFill { get { return Theme().Fill; } }
        private static Color RedPill { get { return Theme().Pill; } }
        private static Color OnPill { get { return Theme().OnPill; } }
        private static Color Border { get { return WithA(Theme().Base, 0.55f); } }
        private static Color HeaderLine { get { return WithA(Theme().Base, 0.6f); } }
        private static Color TabActiveBg { get { return WithA(Theme().Base, 0.22f); } }

        private struct MenuTheme
        {
            public Color Accent, Fill, Pill, OnPill, Base;
        }

        private static readonly MenuTheme[] Themes =
        {
            new MenuTheme { Accent = Hex(0xe8302a), Fill = Hex(0xe0302a), Pill = Hex(0xc8241f),
                            OnPill = Color.white, Base = new Color(0.878f, 0.165f, 0.141f, 1f) },

            new MenuTheme { Accent = Hex(0xf2be28), Fill = Hex(0xf2be28), Pill = Hex(0xd9a514),
                            OnPill = Hex(0x1a1400), Base = Hex(0xf2be28) },

            new MenuTheme { Accent = Hex(0x3ec95a), Fill = Hex(0x3ec95a), Pill = Hex(0x2fa648),
                            OnPill = Color.white, Base = Hex(0x3ec95a) },
        };

        private static MenuTheme Theme()
        {
            int i = CfgColor != null ? (int)CfgColor.Value : 0;
            return Themes[i >= 0 && i < Themes.Length ? i : 0];
        }

        private static Color WithA(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }
        private static readonly Color BgPlain = new Color(0.047f, 0.039f, 0.039f, 0.88f);
        private static readonly Color GlassTint = new Color(0.20f, 0.18f, 0.18f, 1f);
        private static readonly Color White = Color.white;
        private static readonly Color Text1 = Hex(0xf4efea);
        private static readonly Color Line = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color CardBg = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color BoxBg = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color BoxLine = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color TrackBg = new Color(1f, 1f, 1f, 0.14f);

        private static Color A(float a)
        {
            return new Color(1f, 1f, 1f, a);
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        private static Texture2D _dot;
        private static Texture2D _icons;
        private static bool _iconsFailed;
        private static Font _fontReg;
        private static Font _fontSemi;
        private static Font _fontSym;
        private static GUIStyle _ui;
        private static GUIStyle _fieldStyle;
        private static GUIStyle _sliderStyle;
        private static GUIStyle _thumbStyle;
        private static readonly GUIContent _content = new GUIContent();

        private static float _s = 1f;
        private static Vector2 _origin;

        private static void EnsureStyles()
        {
            if (_ui != null)
                return;

            _dot = new Texture2D(64, 64, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave };

            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f));
                    _dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(31.5f - d)));
                }

            _dot.Apply(true);
            _dot.filterMode = FilterMode.Trilinear;

            try
            {
                _fontReg = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 14);
                _fontSemi = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }, 14);
                _fontSym = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Segoe UI", "Arial" }, 14);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Menu: OS font not found: " + ex.Message);
            }

            _ui = new GUIStyle
            {
                font = _fontReg,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            _fieldStyle = new GUIStyle
            {
                font = _fontReg,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _fieldStyle.normal.textColor = Text1;
            _fieldStyle.focused.textColor = White;
            _fieldStyle.hover.textColor = Text1;
            _fieldStyle.active.textColor = White;

            _sliderStyle = new GUIStyle();
            _thumbStyle = new GUIStyle();
        }

        private static Texture2D Icons()
        {
            if (_icons != null || _iconsFailed)
                return _icons;

            _iconsFailed = true;

            try
            {
                var asm = typeof(BodyCamMenu).Assembly;

                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (!n.EndsWith("menu_icons.png", StringComparison.OrdinalIgnoreCase))
                        continue;

                    byte[] png;

                    using (Stream st = asm.GetManifestResourceStream(n))
                    using (var ms = new MemoryStream())
                    {
                        st.CopyTo(ms);
                        png = ms.ToArray();
                    }

                    var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                    if (!ImageConversion.LoadImage(raw, png))
                        break;

                    var tex = new Texture2D(raw.width, raw.height, TextureFormat.RGBA32, true);
                    tex.SetPixels32(raw.GetPixels32());
                    tex.Apply(true, true);
                    UnityEngine.Object.Destroy(raw);

                    tex.filterMode = FilterMode.Trilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _icons = tex;
                    _iconsFailed = false;
                    break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Menu icons error: " + ex.Message);
            }

            return _icons;
        }

        private static Rect P(Rect u)
        {
            float x0 = Mathf.Round(u.x * _s), y0 = Mathf.Round(u.y * _s);
            float x1 = Mathf.Round(u.xMax * _s), y1 = Mathf.Round(u.yMax * _s);
            return new Rect(x0 - _origin.x, y0 - _origin.y, x1 - x0, y1 - y0);
        }

        private static Vector2 MouseU(Event e)
        {
            return (e.mousePosition + _origin) / _s;
        }

        private static void Fill(Rect u, Color c, float radius = 0f)
        {
            GUI.color = White;
            GUI.DrawTexture(P(u), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius * _s);
        }

        private static void HLine(float x, float y, float w, Color c)
        {
            Rect r = P(new Rect(x, y, w, 0f));
            r.height = 1f;
            GUI.color = White;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
        }

        private static void VLine(float x, float y, float h, Color c)
        {
            Rect r = P(new Rect(x, y, 0f, h));
            r.width = 1f;
            GUI.color = White;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
        }

        private static void Outline(Rect u, Color c, float radius)
        {
            GUI.color = White;
            GUI.DrawTexture(P(u), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 1f, radius * _s);
        }

        private static void Circle(Rect u, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(P(u), _dot);
        }

        private static int Px(float size)
        {
            return Mathf.Max(6, Mathf.RoundToInt(size * _s));
        }

        private static void Style(float size, Font font, FontStyle fs, Color c)
        {
            _ui.font = font;
            _ui.fontSize = Px(size);
            _ui.fontStyle = fs;
            _ui.normal.textColor = c;
        }

        private static float Text(string s, float x, float cy, float size, Color c, bool semi = false, bool bold = false)
        {
            if (string.IsNullOrEmpty(s))
                return 0f;

            Style(size, semi ? _fontSemi : _fontReg, bold ? FontStyle.Bold : FontStyle.Normal, c);
            _content.text = s;
            Vector2 sz = _ui.CalcSize(_content);

            float px = Mathf.Round(x * _s) - _origin.x;
            float py = Mathf.Round(cy * _s - sz.y * 0.5f) - _origin.y;
            GUI.color = White;
            GUI.Label(new Rect(px, py, sz.x + 2f, sz.y), _content, _ui);
            return sz.x / _s;
        }

        private static float TextW(string s, float size, bool semi = false, bool bold = false)
        {
            if (string.IsNullOrEmpty(s))
                return 0f;

            Style(size, semi ? _fontSemi : _fontReg, bold ? FontStyle.Bold : FontStyle.Normal, White);
            _content.text = s;
            return _ui.CalcSize(_content).x / _s;
        }

        private static float Sym(string s, float cx, float cy, float size, Color c)
        {
            Style(size, _fontSym, FontStyle.Normal, c);
            _content.text = s;
            Vector2 sz = _ui.CalcSize(_content);
            GUI.color = White;
            GUI.Label(new Rect(Mathf.Round(cx * _s - sz.x * 0.5f) - _origin.x, Mathf.Round(cy * _s - sz.y * 0.5f) - _origin.y,
                sz.x + 2f, sz.y), _content, _ui);
            return sz.x / _s;
        }

        private static float Vcr(string s, float x, float cy, float size, Color c)
        {
            float ps = size * _s;
            float k = ps / BodyCamFontData.RenderSize;
            float top = cy * _s - BodyCamFontData.Ascent * k + BodyCamFontData.CapHeight * k * 0.5f;
            BodyCamFont.Draw(s, Mathf.Round(x * _s) - _origin.x, Mathf.Round(top) - _origin.y, ps, c);
            return BodyCamFont.Width(s, ps) / _s;
        }

        private static bool Click(Rect u)
        {
            return GUI.Button(P(u), GUIContent.none, GUIStyle.none);
        }

        internal static void OnGUI()
        {
            if (!Open)
                return;

            Event e = Event.current;

            if (e == null)
                return;

            KeepCursor();
            EnsureStyles();

            bool toTexture = e.type == EventType.Repaint && BodyCamLens.IsActive();
            RenderTexture prev = RenderTexture.active;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;

            try
            {
                if (toTexture)
                    BodyCamLens.BeginMenu();

                GUI.matrix = Matrix4x4.identity;
                _s = Mathf.Max(0.5f, Mathf.Min(Screen.height / 1080f, Screen.width / 1200f));
                _origin = Vector2.zero;

                float vw = Screen.width / _s;
                float vh = Screen.height / _s;
                Rect win = WindowRect(vw, vh);
                PanelPixels = P(win);

                DrawWindow(win, vw, vh, e, toTexture);
            }
            finally
            {
                _origin = Vector2.zero;
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;

                if (toTexture)
                    RenderTexture.active = prev;
            }
        }

        private static Rect WindowRect(float vw, float vh)
        {
            float px = CfgPosX != null ? Mathf.Clamp01(CfgPosX.Value) : 0.5f;
            float py = CfgPosY != null ? Mathf.Clamp01(CfgPosY.Value) : 0.5f;
            float fx = vw - W, fy = vh - H;

            return new Rect(
                fx > 0f ? fx * px : fx * 0.5f,
                fy > 0f ? fy * py : fy * 0.5f,
                W, H);
        }

        private static void DrawWindow(Rect win, float vw, float vh, Event e, bool lens)
        {
            int lang = Lang;

            if (_capture != null && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode != KeyCode.Escape)
                    Set(_capture, e.keyCode);

                _capture = null;
                e.Use();
            }

            Texture blur = lens ? BodyCamLens.MenuBlur : null;

            if (blur != null)
            {
                GUI.color = White;
                GUI.DrawTexture(P(win), blur, ScaleMode.StretchToFill, true, 0f, GlassTint, 0f, Radius * _s);
            }
            else
            {
                Fill(win, lens ? new Color(BgPlain.r, BgPlain.g, BgPlain.b, 1f) : BgPlain, Radius);
            }

            var hdr = new Rect(win.x, win.y, win.width, HeaderH);
            HLine(win.x, win.y + HeaderH, win.width, HeaderLine);
            Fill(new Rect(win.center.x - 14f, win.y + 4f, 28f, 3f), A(0.2f), 1.5f);

            float cy = win.y + HeaderH * 0.5f + 1f;
            Circle(new Rect(win.x + 12f, cy - 4f, 8f, 8f), Red);

            float x = win.x + 28f;
            x += Vcr("[ ", x, cy, 17f, Red);
            x += Vcr("BODY", x, cy, 17f, Text1);
            x += Vcr("CAMERA", x, cy, 17f, Red);
            x += Vcr(" ]", x, cy, 17f, Red);
            Text("v" + Version(), x + 8f, cy + 1f, 10.5f, A(0.45f));

            var close = new Rect(win.xMax - 12f - 20f, cy - 10f, 20f, 20f);
            Fill(close, A(0.06f), 5f);
            Sym("✕", close.center.x, close.center.y, 11f, A(0.65f));

            if (Click(close))
            {
                Hide();
                return;
            }

            var langBox = new Rect(close.x - 10f - 64f, cy - 11f, 64f, 22f);
            var ru = new Rect(langBox.x + 2f, langBox.y + 2f, 30f, 18f);
            var en = new Rect(ru.xMax, ru.y, 30f, 18f);
            Fill(langBox, A(0.07f), 6f);
            Fill(lang == 0 ? ru : en, RedPill, 4f);
            Text("RU", ru.center.x - TextW("RU", 10.5f, true) * 0.5f, ru.center.y, 10.5f, lang == 0 ? OnPill : A(0.55f), true);
            Text("EN", en.center.x - TextW("EN", 10.5f, true) * 0.5f, en.center.y, 10.5f, lang == 1 ? OnPill : A(0.55f), true);

            if (Click(ru) && CfgLanguage != null)
                Set(CfgLanguage, "RU");

            if (Click(en) && CfgLanguage != null)
                Set(CfgLanguage, "EN");

            bool asking = Time.unscaledTime < _resetAskUntil;
            string resetText = asking ? (lang == 0 ? "ТОЧНО?" : "SURE?") : (lang == 0 ? "СБРОС" : "RESET");
            float resetW = Mathf.Max(54f, TextW(resetText, 10.5f, true) + 16f);
            var reset = new Rect(langBox.x - 8f - resetW, langBox.y, resetW, langBox.height);
            Fill(reset, asking ? RedPill : A(0.07f), 6f);
            Text(resetText, reset.center.x - TextW(resetText, 10.5f, true) * 0.5f, reset.center.y, 10.5f,
                asking ? OnPill : A(0.55f), true);

            if (Click(reset))
            {
                if (asking)
                {
                    _resetAskUntil = 0f;
                    ResetAll();
                }
                else
                {
                    _resetAskUntil = Time.unscaledTime + 2f;
                }
            }

            HandleDrag(hdr, win, vw, vh, e);

            float bodyTop = win.y + HeaderH + 1f;
            float bodyBottom = win.yMax - FooterH;
            VLine(win.x + TabsW, bodyTop, bodyBottom - bodyTop, Line);

            float ty = bodyTop + 8f;
            Texture2D icons = Icons();

            for (int i = 0; i < Sections.Length; i++)
            {
                bool debug = Sections[i] == Plugin.SecDebug;

                if (debug)
                {
                    ty += 7f;
                    HLine(win.x + 10f, ty, TabsW - 20f, Line);
                    ty += 8f;
                }

                var tr = new Rect(win.x + 6f, ty, TabsW - 12f, 26f);
                bool on = i == _tab;

                if (on)
                {
                    Fill(tr, TabActiveBg, 6f);
                    Fill(new Rect(tr.x, tr.y + 4f, 2f, tr.height - 8f), Red, 1f);
                }

                Color tc = on ? White : (debug ? A(0.42f) : A(0.72f));

                if (icons != null)
                {
                    GUI.color = on ? Red : new Color(1f, 1f, 1f, debug ? 0.42f : 0.62f);
                    GUI.DrawTextureWithTexCoords(P(new Rect(tr.x + 9f, tr.center.y - 7f, 14f, 14f)), icons,
                        new Rect(i / 10f, 0f, 0.1f, 1f), true);
                    GUI.color = White;
                }

                string[] names;
                string name = BodyCamMenuText.Tabs.TryGetValue(Sections[i], out names) ? names[lang] : Sections[i];
                Text(name, tr.x + 31f, tr.center.y, 11.5f, tc);

                if (Click(tr) && !on)
                {
                    _tab = i;
                    _scroll = 0f;
                    _capture = null;
                    GUIUtility.keyboardControl = 0;
                }

                ty += 27f;
            }

            var list = new Rect(win.x + TabsW + 1f, bodyTop, win.width - TabsW - 1f, bodyBottom - bodyTop);
            string hovered = DrawList(list, lang, e);

            float fy = win.yMax - FooterH;
            HLine(win.x, fy, win.width, Line);

            float bw = TextW("RedSparrow", 10.5f, true);
            float bx = win.xMax - 12f - bw;
            Text("RedSparrow", bx, fy + FooterH * 0.5f, 10.5f, Red, true);
            Text("by.", bx - TextW("by.", 10.5f), fy + FooterH * 0.5f, 10.5f, A(0.5f));

            ConfigEntryBase he;

            if (hovered == ProfileKey)
            {
                float dx = win.x + 12f;
                string title = BodyCamMenuText.Profiles[lang];
                float nw = Text(title, dx, fy + 15f, 10.5f, White, true);
                if (!string.IsNullOrEmpty(BodyCamMenuText.ProfileDesc[lang]))
                    Text(" — " + BodyCamMenuText.ProfileDesc[lang], dx + nw, fy + 15f, 10.5f, A(0.8f));
            }
            else if (hovered != null && _entries.TryGetValue(hovered, out he))
            {
                float dx = win.x + 12f;
                string[] desc;
                float nw = Text(hovered, dx, fy + 15f, 10.5f, White, true);

                if (BodyCamMenuText.Desc.TryGetValue(hovered, out desc))
                    Text(" — " + desc[lang], dx + nw, fy + 15f, 10.5f, A(0.8f));

                Text(FooterInfo(he, lang), dx, fy + 30f, 10.5f, A(0.45f));
            }

            Outline(win, Border, Radius);
            GUI.color = White;
        }

        private static string DrawList(Rect list, int lang, Event e)
        {
            string hovered = null;
            Vector2 mouse = MouseU(e);
            bool mouseIn = list.Contains(mouse) && !_drag;

            if (e.type == EventType.ScrollWheel && mouseIn)
            {
                _scroll += e.delta.y * 18f;
                e.Use();
            }

            float maxScroll = Mathf.Max(0f, _contentH - list.height);
            _scroll = Mathf.Clamp(_scroll, 0f, maxScroll);

            Rect clip = P(list);
            GUI.BeginClip(clip);
            _origin = new Vector2(clip.x, clip.y);

            try
            {
                float lx = list.x + 10f;
                float lw = list.width - 22f;
                float y = list.y + 8f - _scroll;
                float top = y;

                string[][] groups;

                if (!BodyCamMenuText.Groups.TryGetValue(Sections[_tab], out groups))
                    groups = new string[0][];

                var used = new HashSet<string>();

                foreach (var tabGroups in BodyCamMenuText.Groups.Values)
                    for (int g = 0; g < tabGroups.Length; g++)
                        for (int k = 2; k < tabGroups[g].Length; k++)
                            used.Add(tabGroups[g][k]);

                var rest = new List<string> { "", "" };
                List<string> keys;

                if (_sectionKeys.TryGetValue(Sections[_tab], out keys))
                    for (int k = 0; k < keys.Count; k++)
                        if (!used.Contains(keys[k]))
                            rest.Add(keys[k]);

                for (int g = 0; g <= groups.Length; g++)
                {
                    string[] grp = g < groups.Length ? groups[g] : rest.ToArray();

                    var rows = new List<ConfigEntryBase>();
                    var names = new List<string>();

                    for (int k = 2; k < grp.Length; k++)
                    {
                        ConfigEntryBase ce;

                        if (_entries.TryGetValue(grp[k], out ce))
                        {
                            rows.Add(ce);
                            names.Add(grp[k]);
                        }
                    }

                    if (rows.Count == 0)
                        continue;

                    string title = grp[lang == 0 ? 0 : 1];

                    if (!string.IsNullOrEmpty(title))
                    {
                        Text(title, lx + 4f, y + 7f, 9.5f, A(0.5f), true, true);
                        y += 18f;
                    }

                    float h = 0f;

                    for (int r = 0; r < rows.Count; r++)
                        h += rows[r].SettingType == typeof(float) ? SliderRowH : RowH;

                    var card = new Rect(lx, y, lw, h);
                    Fill(card, CardBg, 9f);
                    Outline(card, Line, 9f);

                    float ry = y;

                    for (int r = 0; r < rows.Count; r++)
                    {
                        float rh = rows[r].SettingType == typeof(float) ? SliderRowH : RowH;
                        var row = new Rect(lx + 10f, ry, lw - 20f, rh);

                        if (r > 0)
                            HLine(row.x, ry, row.width, Line);

                        if (mouseIn && row.Contains(mouse))
                            hovered = names[r];

                        if (row.yMax > list.y && row.y < list.yMax)
                            DrawRow(names[r], rows[r], row, lang);
                        ry += rh;
                    }

                    y += h + 10f;
                }

                if (Sections[_tab] == Plugin.SecGeneral)
                    y = DrawProfiles(lx, lw, y, list, lang, mouse, mouseIn, ref hovered);

                _contentH = y - top + 2f;

                if (maxScroll > 0.5f)
                {
                    float track = list.height - 16f;
                    float bar = Mathf.Max(24f, track * list.height / _contentH);
                    float by = list.y + 8f + (track - bar) * (_scroll / maxScroll);
                    Fill(new Rect(list.xMax - 6f, by, 3f, bar), A(0.22f), 1.5f);
                }
            }
            finally
            {
                GUI.EndClip();
                _origin = Vector2.zero;
            }

            return hovered;
        }

        private const string ProfileKey = "\u0001profiles";
        private const int ProfileCount = 10;
        private static int _profileMsgSlot = -1;
        private static bool _profileMsgSaved;
        private static float _profileMsgUntil;

        private static float _resetAskUntil;

        private static void ResetAll()
        {
            if (_config == null)
                return;

            int n = 0;

            try
            {
                foreach (var pair in _config)
                {
                    ConfigEntryBase e = pair.Value;

                    if (e == null || Array.IndexOf(Hidden, e.Definition.Key) >= 0)
                        continue;

                    if (!Equals(e.BoxedValue, e.DefaultValue))
                    {
                        e.BoxedValue = e.DefaultValue;
                        n++;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Reset failed: " + ex.Message);
            }

            _dirty = true;
            _capture = null;
            GUIUtility.keyboardControl = 0;
            Plugin.Log.LogInfo("[BodyCamera] Settings reset to defaults (" + n + " changed).");
        }

        private static string ProfilePath(int slot)
        {
            string dir = _config != null ? Path.GetDirectoryName(_config.ConfigFilePath) : "";
            return Path.Combine(dir ?? "", "BodyCamera_Profile" + slot + ".cfg");
        }

        private static bool InProfile(ConfigEntryBase e)
        {
            return e != null && e.SettingType != typeof(KeyCode);
        }

        private static void SaveProfile(int slot)
        {
            try
            {
                var lines = new List<string>();
                var seen = new HashSet<ConfigEntryBase>();

                foreach (ConfigEntryBase e in _entries.Values)
                {
                    if (!InProfile(e) || !seen.Add(e))
                        continue;

                    lines.Add(e.Definition.Section + "|" + e.Definition.Key + "=" + e.GetSerializedValue());
                }

                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(ProfilePath(slot), lines.ToArray(), new System.Text.UTF8Encoding(false));
                _profileMsgSlot = slot;
                _profileMsgSaved = true;
                _profileMsgUntil = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo("[BodyCamera] Profile " + slot + " saved (" + lines.Count + " settings).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Could not save profile " + slot + ": " + ex.Message);
            }
        }

        private static void LoadProfile(int slot)
        {
            try
            {
                string path = ProfilePath(slot);

                if (!File.Exists(path))
                    return;

                var byName = new Dictionary<string, ConfigEntryBase>();

                foreach (ConfigEntryBase e in _entries.Values)
                {
                    if (InProfile(e))
                        byName[e.Definition.Section + "|" + e.Definition.Key] = e;
                }

                int n = 0;

                foreach (string raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');

                    if (eq <= 0)
                        continue;

                    ConfigEntryBase e;

                    if (!byName.TryGetValue(raw.Substring(0, eq), out e))
                        continue;

                    try
                    {
                        e.SetSerializedValue(raw.Substring(eq + 1));
                        n++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("[BodyCamera] Profile " + slot + ": bad value for " + e.Definition.Key + ": " + ex.Message);
                    }
                }

                _dirty = true;
                _capture = null;
                GUIUtility.keyboardControl = 0;
                _profileMsgSlot = slot;
                _profileMsgSaved = false;
                _profileMsgUntil = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo("[BodyCamera] Profile " + slot + " loaded (" + n + " settings).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Could not load profile " + slot + ": " + ex.Message);
            }
        }

        private static void LoadProfileByKey(int slot)
        {
            if (!File.Exists(ProfilePath(slot)))
            {
                Plugin.Log.LogInfo("[BodyCamera] Profile " + slot + " is empty.");
                return;
            }

            if (Open || _config == null)
            {
                LoadProfile(slot);
                return;
            }

            bool save = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;

            try
            {
                LoadProfile(slot);
            }
            finally
            {
                _config.SaveOnConfigSet = save;
            }

            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Could not save settings: " + ex.Message);
            }

            _dirty = false;
        }

        private static float SmallButton(string s, float right, float cy, bool enabled, out bool clicked)
        {
            float w = TextW(s, 10.5f) + 20f;
            var r = new Rect(right - w, cy - 10f, w, 20f);
            Box(r);
            Text(s, r.center.x - TextW(s, 10.5f) * 0.5f, cy, 10.5f, enabled ? Text1 : A(0.3f));
            clicked = Click(r) && enabled;
            return r.x;
        }

        private static float DrawProfiles(float lx, float lw, float y, Rect list, int lang,
            Vector2 mouse, bool mouseIn, ref string hovered)
        {
            Text(BodyCamMenuText.Profiles[lang], lx + 4f, y + 7f, 9.5f, A(0.5f), true, true);
            y += 18f;

            var card = new Rect(lx, y, lw, RowH * ProfileCount);
            Fill(card, CardBg, 9f);
            Outline(card, Line, 9f);

            for (int i = 0; i < ProfileCount; i++)
            {
                int slot = i + 1;
                var row = new Rect(lx + 10f, y + i * RowH, lw - 20f, RowH);

                if (i > 0)
                    HLine(row.x, row.y, row.width, Line);

                if (mouseIn && row.Contains(mouse))
                    hovered = ProfileKey;

                if (row.yMax <= list.y || row.y >= list.yMax)
                    continue;

                float cy = row.center.y;
                bool has = File.Exists(ProfilePath(slot));
                float nw = Text(BodyCamMenuText.Slot[lang] + " " + slot, row.x, cy, 11.5f, Text1);

                if (_profileMsgSlot == slot && Time.unscaledTime < _profileMsgUntil)
                    Text(_profileMsgSaved ? BodyCamMenuText.Saved[lang] : BodyCamMenuText.Loaded[lang],
                        row.x + nw + 10f, cy, 10.5f, Red);
                else if (!has)
                    Text(BodyCamMenuText.Empty[lang], row.x + nw + 10f, cy, 10.5f, A(0.35f));

                bool load, save;
                float lx2 = SmallButton(BodyCamMenuText.Load[lang], row.xMax, cy, has, out load);
                float sx = SmallButton(BodyCamMenuText.Save[lang], lx2 - 6f, cy, true, out save);

                if (CfgProfileKeys[i] != null)
                    KeyBox(CfgProfileKeys[i], sx - 6f, cy, lang);

                if (save)
                    SaveProfile(slot);
                else if (load)
                    LoadProfile(slot);
            }

            return y + RowH * ProfileCount + 10f;
        }

        private static void DrawRow(string key, ConfigEntryBase entry, Rect row, int lang)
        {
            Type t = entry.SettingType;
            bool slider = t == typeof(float);
            float cy = slider ? row.y + 15f : row.center.y;
            float rstX = row.xMax - 12f;
            float right = rstX - 8f;

            Text(key, row.x, cy, 11.5f, Text1);

            if (slider)
                Slider(entry, row, right, cy);
            else if (t == typeof(bool))
                Toggle(entry, right, cy);
            else if (t == typeof(KeyCode))
                KeyBox(entry, right, cy, lang);
            else if (t.IsEnum)
                EnumBox(entry, right, cy);
            else if (t == typeof(string))
                TextBox(entry, right, cy, lang);

            bool changed = !SameValue(entry);
            var rst = new Rect(rstX, cy - 8f, 12f, 16f);
            Sym("↺", rst.center.x, cy, 10.5f, changed ? Red : A(0.25f));

            if (Click(rst) && changed)
            {
                if (_capture == entry)
                    _capture = null;

                GUIUtility.keyboardControl = 0;
                Set(entry, entry.DefaultValue);
            }
        }

        private static void Slider(ConfigEntryBase entry, Rect row, float right, float cy)
        {
            float min = 0f, max = 1f;
            var range = entry.Description != null ? entry.Description.AcceptableValues as AcceptableValueRange<float> : null;

            if (range != null)
            {
                min = range.MinValue;
                max = range.MaxValue;
            }

            float span = max - min;
            float step = span >= 20f ? 0.1f : 0.01f;
            float cur = (float)entry.BoxedValue;

            string v = cur.ToString(step < 0.05f ? "0.00" : "0.0", CultureInfo.InvariantCulture);
            Text(v, right - TextW(v, 10.5f), cy, 10.5f, A(0.65f));

            float ty = row.y + 33f;
            float x = row.x;
            float w = row.xMax - row.x;

            _thumbStyle.fixedWidth = 12f * _s;
            _thumbStyle.fixedHeight = 16f * _s;
            float nv = GUI.HorizontalSlider(P(new Rect(x - 6f, ty - 8f, w + 12f, 16f)), cur, min, max, _sliderStyle, _thumbStyle);
            nv = Mathf.Clamp(Mathf.Round(nv / step) * step, min, max);

            if (Mathf.Abs(nv - cur) > step * 0.5f)
            {
                Set(entry, nv);
                cur = nv;
            }

            float p = span > 0f ? Mathf.Clamp01((cur - min) / span) : 0f;
            Fill(new Rect(x, ty - 1.5f, w, 3f), TrackBg, 1.5f);

            if (p > 0.001f)
                Fill(new Rect(x, ty - 1.5f, Mathf.Max(3f, w * p), 3f), RedFill, 1.5f);

            Circle(new Rect(x + w * p - 7f, ty - 6f, 14f, 14f), new Color(0f, 0f, 0f, 0.35f));
            Circle(new Rect(x + w * p - 6f, ty - 6f, 12f, 12f), White);
        }

        private static void Toggle(ConfigEntryBase entry, float right, float cy)
        {
            bool on = (bool)entry.BoxedValue;
            var r = new Rect(right - 26f, cy - 7f, 26f, 14f);

            Fill(r, on ? RedPill : A(0.16f), 7f);
            Circle(on ? new Rect(r.xMax - 12f, r.y + 2f, 10f, 10f) : new Rect(r.x + 2f, r.y + 2f, 10f, 10f),
                on ? White : Hex(0xcfc8c3));

            if (Click(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f)))
                Set(entry, !on);
        }

        private static void Box(Rect r)
        {
            Fill(r, BoxBg, 5f);
            Outline(r, BoxLine, 5f);
        }

        private static void KeyBox(ConfigEntryBase entry, float right, float cy, int lang)
        {
            bool waiting = _capture == entry;
            string s = waiting ? BodyCamMenuText.PressKey[lang] : entry.BoxedValue.ToString();
            float w = Mathf.Max(34f, TextW(s, 10.5f) + 20f);
            var r = new Rect(right - w, cy - 10f, w, 20f);

            Box(r);

            if (waiting)
                Outline(r, Red, 5f);

            if (!waiting || Mathf.Repeat(Time.unscaledTime, 1f) < 0.65f)
                Text(s, r.center.x - TextW(s, 10.5f) * 0.5f, cy, 10.5f, waiting ? Red : Text1);

            if (Click(r))
            {
                _capture = waiting ? null : entry;
                GUIUtility.keyboardControl = 0;
            }
        }

        private static void EnumBox(ConfigEntryBase entry, float right, float cy)
        {
            var r = new Rect(right - 112f, cy - 10f, 112f, 20f);
            Box(r);

            Sym("‹", r.x + 9f, cy, 11f, A(0.5f));
            Sym("›", r.xMax - 9f, cy, 11f, A(0.5f));

            string v = entry.BoxedValue.ToString().Replace('_', ' ');
            Text(v, r.center.x - TextW(v, 10.5f) * 0.5f, cy, 10.5f, Text1);

            Array values = Enum.GetValues(entry.SettingType);

            if (values.Length == 0)
                return;

            int idx = Array.IndexOf(values, entry.BoxedValue);

            if (Click(new Rect(r.x, r.y, r.width * 0.5f, r.height)))
                Set(entry, values.GetValue((idx - 1 + values.Length) % values.Length));
            else if (Click(new Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f, r.height)))
                Set(entry, values.GetValue((idx + 1) % values.Length));
        }

        private static void TextBox(ConfigEntryBase entry, float right, float cy, int lang)
        {
            var r = new Rect(right - 150f, cy - 10f, 150f, 20f);
            string cur = (string)entry.BoxedValue ?? "";

            Fill(r, A(0.06f), 5f);
            Outline(r, A(0.12f), 5f);

            if (cur.Length == 0 && GUIUtility.keyboardControl == 0)
                Text(BodyCamMenuText.Empty[lang], r.x + 8f, cy, 10.5f, A(0.35f));

            _fieldStyle.fontSize = Px(10.5f);
            _fieldStyle.padding = new RectOffset(Mathf.RoundToInt(8f * _s), Mathf.RoundToInt(8f * _s), 0, 0);
            GUI.color = White;
            string nv = GUI.TextField(P(r), cur, 32, _fieldStyle);

            if (nv != cur)
                Set(entry, nv);
        }

        private static void HandleDrag(Rect hdr, Rect win, float vw, float vh, Event e)
        {
            Vector2 m = MouseU(e);

            if (e.type == EventType.MouseDown && e.button == 0 && hdr.Contains(m))
            {
                _drag = true;
                _dragOffset = m - win.position;
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
            else if (_drag && e.type == EventType.MouseDrag)
            {
                Vector2 p = m - _dragOffset;
                float fx = vw - W, fy = vh - H;

                if (fx > 0f && CfgPosX != null)
                    Set(CfgPosX, Mathf.Clamp01(p.x / fx));

                if (fy > 0f && CfgPosY != null)
                    Set(CfgPosY, Mathf.Clamp01(p.y / fy));

                e.Use();
            }
            else if (_drag && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                _drag = false;
            }
        }

        private static bool SameValue(ConfigEntryBase entry)
        {
            if (entry.SettingType == typeof(float))
                return Mathf.Abs((float)entry.BoxedValue - (float)entry.DefaultValue) < 0.0005f;

            return Equals(entry.BoxedValue, entry.DefaultValue);
        }

        private static string FooterInfo(ConfigEntryBase entry, int lang)
        {
            string def = BodyCamMenuText.Default[lang];
            var range = entry.Description != null ? entry.Description.AcceptableValues as AcceptableValueRange<float> : null;

            if (range != null)
            {
                string f = range.MaxValue - range.MinValue >= 20f ? "0.0" : "0.00";
                return range.MinValue.ToString(f, CultureInfo.InvariantCulture) + " … " +
                       range.MaxValue.ToString(f, CultureInfo.InvariantCulture) + "  ·  " + def + " " +
                       ((float)entry.DefaultValue).ToString(f, CultureInfo.InvariantCulture);
            }

            object d = entry.DefaultValue;

            if (d is bool)
                return def + ": " + ((bool)d ? BodyCamMenuText.On[lang] : BodyCamMenuText.Off[lang]);

            string s = d != null ? d.ToString() : "";
            return def + ": " + (s.Length > 0 ? s : BodyCamMenuText.Empty[lang]);
        }

        private static string Version()
        {
            if (_version != null)
                return _version;

            try
            {
                var a = (BepInPlugin)Attribute.GetCustomAttribute(typeof(Plugin), typeof(BepInPlugin));
                _version = a != null ? a.Version.ToString() : "";
            }
            catch
            {
                _version = "";
            }

            return _version;
        }
    }

    internal sealed class BodyCamMenuInputPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.InputSystem.InputManager).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            return !BodyCamMenu.BlockGameInput;
        }
    }

    internal sealed class BodyCamMenuFixedInputPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.InputSystem.InputManager).GetMethod(
                "FixedUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            return !BodyCamMenu.BlockGameInput;
        }
    }
}

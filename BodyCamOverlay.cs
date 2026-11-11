using System;
using System.Reflection;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal enum OverlayStyle
    {
        Action_cam_v1,
        Action_cam_v2
    }

    internal enum OverlayCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    internal static class BodyCamOverlay
    {
        internal static ConfigEntry<OverlayStyle> CfgStyle;
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<bool> CfgConnected;
        internal static ConfigEntry<string> CfgName;
        internal static ConfigEntry<string> CfgDevice;
        internal static ConfigEntry<bool> CfgRaidTime;
        internal static ConfigEntry<float> CfgScale;
        internal static ConfigEntry<bool> CfgSound;
        internal static ConfigEntry<float> CfgBatteryDrain;
        internal static ConfigEntry<float> CfgCurve;

        private const int BatteryBars = 4;
        private static GameWorld _batteryWorld;
        private static float _batteryStart;
        private static readonly float[] CornerUsed = new float[4];

        private const float ConnectedTime = 1.6f;
        private const float ConnectedFade = 0.35f;
        private const float MarginX = 90f;
        private const float MarginY = 72f;
        private const float TextSize = 30f;
        private const float NameSize = 24f;
        private const float ConnectedSize = 36f;
        private const float MagSize = 20f;
        private const float AmmoSize = 14f;

        private static float _connectedUntil = -100f;
        private static bool _overlaySoundPending;

        private static Texture2D _dot;

        private static string _clockText = "";
        private static DateTime _clockTime = DateTime.Now;
        private static int _clockSecond = -1;
        private static float _nextClock;

        private static FieldInfo _gameDateTimeField;
        private static MethodInfo _calculate;
        private static bool _timeLookup;

        private static string _nameSource;
        private static float _nameMaxW = -1f;
        private static float _nameBase = -1f;
        private static string _nameFit = "";
        private static float _nameSize;

        private static readonly Color Green = new Color(0.25f, 1f, 0.35f, 1f);
        private static readonly Color Red = new Color(1f, 0.15f, 0.12f, 1f);
        private static readonly Color Orange = new Color(1f, 0.58f, 0.12f, 1f);
        private static readonly Color Text = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color Pink = new Color(1f, 0.30f, 0.55f, 0.35f);
        private static readonly Color Cyan = new Color(0.30f, 0.85f, 1f, 0.35f);

        internal static void BindConfig()
        {
            CfgStyle = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Style", OverlayStyle.Action_cam_v2,
                "Action cam v1: time, battery, REC, PLAY. Action cam v2: date, name, device line and icon.",
                null, 101, null, null);

            CfgEnabled = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Enabled", true,
                "Overlay text (v1 / v2); the mag check count is a separate setting.",
                null, 100, null, null);

            CfgConnected = ConfigMigration.Bind(
                Plugin.SecOverlay, "Show Cam Connected", true,
                "CAM CONNECTED when the camera switches on, also with the overlay off.",
                null, 99, null, null);

            CfgName = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Name", "",
                "Name in the overlay. Empty: none (v1), your nickname (v2).",
                null, 90, null, null);

            CfgDevice = ConfigMigration.Bind(
                Plugin.SecOverlay, "Device Text", "BODY CAM 3 X60A8615D",
                "Device line of Action cam v2. Empty = none.",
                null, 88, null, null);

            CfgBatteryDrain = ConfigMigration.Bind(
                Plugin.SecOverlay, "Battery Drain", 35.30f,
                "Minutes of raid per battery bar.",
                new AcceptableValueRange<float>(0f, 60f), 84, null, null);

            CfgRaidTime = ConfigMigration.Bind(
                Plugin.SecOverlay, "Use Raid Time", true,
                "On: raid time of day. Off: real PC time.",
                null, 80, null, null);

            CfgSound = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Sound", true,
                "Short beep when the overlay appears.",
                null, 75, null, null);

            CfgScale = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Scale", 1.29f,
                "Size of the overlay text and icons.",
                new AcceptableValueRange<float>(0.5f, 2.0f), 70, null, null);

            CfgCurve = ConfigMigration.Bind(
                Plugin.SecOverlay, "Overlay Curve", 0.00f,
                "Bends the overlay like the lens (needs Lens Effects).",
                new AcceptableValueRange<float>(0f, 1f), 68, null, null);
        }

        private static bool V2
        {
            get { return CfgStyle != null && CfgStyle.Value == OverlayStyle.Action_cam_v2; }
        }

        private static float _camUntil = -100f;
        private static string _camText = "";

        internal static void TriggerCam(int n)
        {
            _camText = "CAM " + n;
            _camUntil = Time.unscaledTime + ConnectedTime;
        }

        private static void DrawCamLabel()
        {
            float now = Time.unscaledTime;

            if (now >= _camUntil || !Plugin.Enabled.Value)
                return;

            GameWorld world = Singleton<GameWorld>.Instance;

            if (world == null || Plugin.StartedWorld != world)
                return;

            Player player = world.MainPlayer;

            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            if (Cursor.visible && !BodyCamMenu.Open)
                return;

            float screen = Mathf.Max(0.3f, Screen.height / 1080f);
            float size = ConnectedSize * screen;
            float a = Mathf.Clamp01((_camUntil - now) / ConnectedFade);
            Color oldColor = GUI.color;

            try
            {
                Init();
                DrawVhs(_camText, 40f * screen,
                    Screen.height - 40f * screen - LineHeight(size), size, Green, a);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        internal static void TriggerConnected()
        {
            _connectedUntil = Time.unscaledTime + ConnectedTime;
            _overlaySoundPending = true;
        }

        private static bool ShouldDraw(out Player player, out GameWorld world)
        {
            player = null;
            world = Singleton<GameWorld>.Instance;

            bool text = CfgEnabled != null && CfgEnabled.Value;
            bool mag = BodyCamMagCheck.CfgEnabled != null && BodyCamMagCheck.CfgEnabled.Value;
            bool connected = CfgConnected != null && CfgConnected.Value;

            if ((!text && !mag && !connected) || !Plugin.Enabled.Value)
                return false;

            if (BodyCamGunCam.Active)
                return false;

            if (world == null || Plugin.StartedWorld != world)
                return false;

            player = world.MainPlayer;

            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return false;

            if (Cursor.visible && !BodyCamMenu.Open)
                return false;

            return true;
        }

        internal static void Draw()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            BodyCamMagCheck.Tick();

            DrawCamLabel();

            Player player;
            GameWorld world;

            if (!ShouldDraw(out player, out world))
                return;

            float screen = Mathf.Max(0.3f, Screen.height / 1080f);
            float s = screen * CfgScale.Value;
            Init();

            float now = Time.unscaledTime;
            Color oldColor = GUI.color;

            bool showText = CfgEnabled != null && CfgEnabled.Value;

            try
            {
                if (CfgConnected != null && CfgConnected.Value && now < _connectedUntil)
                {
                    float left = _connectedUntil - now;
                    float a = Mathf.Clamp01(left / ConnectedFade);

                    float size = ConnectedSize * screen;
                    float w = TextWidth("CAM CONNECTED", size);

                    DrawVhs("CAM CONNECTED", Screen.width * 0.5f - w * 0.5f,
                        Screen.height * 0.42f - LineHeight(size) * 0.5f, size, Green, a);
                    return;
                }

                if (_overlaySoundPending && showText)
                {
                    _overlaySoundPending = false;

                    if (CfgSound.Value && now - _connectedUntil < 0.5f)
                        BodyCamBeep.PlayOverlay();
                }

                CornerUsed[0] = CornerUsed[1] = CornerUsed[2] = CornerUsed[3] = 0f;

                if (!showText)
                {
                }
                else if (V2)
                {
                    V2Info(world, player, now, s);
                }
                else
                {
                    DrawPlay(s, screen);
                    DrawInfo(world, now, s, screen);
                    DrawRec(now, s, screen);
                }

                DrawMagCheck(screen);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static void DrawInfo(GameWorld world, float now, float s, float screen)
        {
            const bool battery = true;
            const bool info = true;

            if (now >= _nextClock || _clockText.Length == 0)
            {
                _nextClock = now + 0.25f;
                UpdateClock(world);
            }

            float size = TextSize * s;
            float lineH = LineHeight(size);
            float cap = CapHeight(size);
            string clock = info ? _clockText : "";
            float timeW = info ? TextWidth(clock, size) : 0f;

            float batH = cap * 1.05f;
            float batW = batH * 2.25f;
            float gap = battery && info ? size * 0.6f : 0f;
            float line1W = (battery ? batW : 0f) + gap + timeW;

            string name = info ? (CfgName.Value ?? "").Trim() : "";
            string nameFit = "";
            float nameSize = NameSize * s;

            if (name.Length > 0 && timeW > 0f)
            {
                FitName(name, timeW, nameSize);
                nameFit = _nameFit;
                nameSize = _nameSize;
            }

            float nameH = nameFit.Length > 0 ? LineHeight(nameSize) : 0f;
            float blockH = lineH + (nameH > 0f ? nameH * 0.95f : 0f);

            OverlayCorner corner = OverlayCorner.TopRight;
            bool right = corner == OverlayCorner.TopRight || corner == OverlayCorner.BottomRight;
            Vector2 pos = Place(corner, line1W, blockH, s, screen);

            float x = pos.x;

            if (battery)
            {
                if (!ReferenceEquals(_batteryWorld, world))
                {
                    _batteryWorld = world;
                    _batteryStart = now;
                }

                float capTop = pos.y + (Ascent(size) - cap);
                DrawBattery(new Rect(x, capTop + (cap - batH) * 0.5f, batW, batH), BatteryLevel(now));
                x += batW + gap;
            }

            DrawVhs(clock, x, pos.y, size, Text, 1f);

            if (nameFit.Length > 0)
            {
                float nw = TextWidth(nameFit, nameSize);
                float nx = right ? x + timeW - nw : x;
                DrawVhs(nameFit, nx, pos.y + lineH * 0.95f, nameSize, Text, 1f);
            }
        }

        private static void FitName(string name, float maxW, float baseSize)
        {
            if (name == _nameSource && Mathf.Abs(maxW - _nameMaxW) < 0.5f && Mathf.Abs(baseSize - _nameBase) < 0.01f)
                return;

            _nameSource = name;
            _nameMaxW = maxW;
            _nameBase = baseSize;

            float size = baseSize;
            float w = TextWidth(name, size);

            if (w > maxW)
                size = Mathf.Max(baseSize * 0.7f, baseSize * maxW / w);

            string fit = name;

            while (fit.Length > 1 && TextWidth(fit, size) > maxW)
                fit = fit.Substring(0, fit.Length - 1);

            _nameFit = fit.TrimEnd();
            _nameSize = size;
        }

        private static Texture2D _triangle;

        private static void DrawPlay(float s, float screen)
        {
            float size = TextSize * s;
            float cap = CapHeight(size);
            float textW = TextWidth("PLAY", size);
            float triH = Mathf.Round(cap * 0.72f);
            float triW = Mathf.Round(triH * 0.85f);
            float gap = Mathf.Round(size * 0.18f);
            Vector2 pos = Place(OverlayCorner.TopLeft, textW + gap + triW, LineHeight(size), s, screen);

            DrawVhs("PLAY", pos.x, pos.y, size, Text, 1f);

            if (_triangle == null)
                _triangle = BuildTriangle(64);

            float capTop = pos.y + (Ascent(size) - cap);
            Rect r = new Rect(Mathf.Round(pos.x + textW + gap), Mathf.Round(capTop), triW, triH);
            DrawTexLayers(r, Mathf.Max(1f, Mathf.Round(size / 18f)), Mathf.Max(1f, Mathf.Round(size / 30f)), _triangle, Text);
        }

        private static Texture2D BuildTriangle(int n)
        {
            var px = new Color32[n * n];
            Vector2 a = new Vector2(n * 0.08f, n * 0.06f);
            Vector2 b = new Vector2(n * 0.08f, n * 0.94f);
            Vector2 c = new Vector2(n * 0.94f, n * 0.5f);

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Max(EdgeDist(p, a, b), Mathf.Max(EdgeDist(p, b, c), EdgeDist(p, c, a)));
                    float al = Mathf.Clamp01(0.5f - d);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(al * 255f));
                }
            }

            return MakeTex(n, n, px, null);
        }

        private static float EdgeDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 e = (b - a).normalized;
            Vector2 nrm = new Vector2(-e.y, e.x);
            return Vector2.Dot(p - a, nrm);
        }

        private static void DrawRec(float now, float s, float screen)
        {
            float size = TextSize * s;
            float cap = CapHeight(size);
            float textW = TextWidth("REC", size);
            float dot = cap * 0.85f;
            float gap = cap * 0.45f;
            Vector2 rec = Place(OverlayCorner.BottomRight, dot + gap + textW, LineHeight(size), s, screen);

            float x = rec.x + dot + gap;
            DrawVhs("REC", x, rec.y, size, Text, 1f);

            if (Mathf.Repeat(now, 1f) < 0.6f)
            {
                float capTop = rec.y + (Ascent(size) - cap);
                Rect r = new Rect(rec.x, capTop + (cap - dot) * 0.5f, dot, dot);
                float o = Mathf.Max(1f, size / 18f);

                GUI.color = Shadow;
                GUI.DrawTexture(new Rect(r.x + o, r.y + o, r.width, r.height), _dot);
                GUI.color = Red;
                GUI.DrawTexture(r, _dot);
            }
        }

        private const float V2Margin = 28f;
        private const float V2TextSize = 24f;
        private static Texture2D _v2Icon;
        private static bool _v2IconTried;

        private static void V2Info(GameWorld world, Player player, float now, float s)
        {
            if (now >= _nextClock || _clockText.Length == 0)
            {
                _nextClock = now + 0.25f;
                UpdateClock(world);
            }

            string name = (CfgName.Value ?? "").Trim();

            if (name.Length == 0 && player != null && player.Profile != null)
                name = player.Profile.Nickname ?? "";

            DateTime t = _clockTime;
            string line1 = t.ToString("yyyy-MM-dd  HH:mm:ss") + (name.Length > 0 ? "  " + name : "");
            string line2 = (CfgDevice.Value ?? "").Trim().ToUpperInvariant();

            float size = V2TextSize * s;
            float w1 = TextWidth(line1, size);
            float w2 = line2.Length > 0 ? TextWidth(line2, size) : 0f;
            float lineH = LineHeight(size);
            float blockH = line2.Length > 0 ? lineH * 2f : lineH;

            Texture2D icon = V2Icon();
            float iconSize = icon != null ? blockH * 0.9f : 0f;
            float iconGap = icon != null ? 12f * s : 0f;

            float margin = V2Margin * s;
            float right = Screen.width - margin - iconSize - iconGap;
            float y = margin + TopCurveShift(Mathf.Max(0.3f, Screen.height / 1080f));

            DrawVhs(line1, right - w1, y, size, Text, 1f);

            if (line2.Length > 0)
                DrawVhs(line2, right - w2, y + lineH, size, Text, 1f);

            if (icon != null)
            {
                Rect r = new Rect(Screen.width - margin - iconSize, y + (blockH - iconSize) * 0.5f, iconSize, iconSize);
                float aspect = icon.height > 0 ? (float)icon.width / icon.height : 1f;

                if (aspect > 1f)
                {
                    float h = r.width / aspect;
                    r = new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);
                }
                else
                {
                    float w = r.height * aspect;
                    r = new Rect(r.xMax - w, r.y, w, r.height);
                }

                GUI.color = Color.white;
                GUI.DrawTexture(r, icon);
            }
        }

        private static Texture2D V2Icon()
        {
            if (_v2IconTried)
                return _v2Icon;

            _v2IconTried = true;

            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(BodyCamOverlay).Assembly.Location), "icon.png");

                if (!System.IO.File.Exists(path))
                {
                    Plugin.Log.LogInfo("[BodyCamera] Overlay v2: no icon.png in the plugin folder.");
                    return null;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);

                if (!ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(path)))
                {
                    Plugin.Log.LogWarning("[BodyCamera] Could not read overlay icon: " + path);
                    return null;
                }

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.hideFlags = HideFlags.HideAndDontSave;
                _v2Icon = tex;
                Plugin.Log.LogInfo("[BodyCamera] Overlay icon loaded: " + path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Overlay icon error: " + ex.Message);
            }

            return _v2Icon;
        }

        private static void DrawMagCheck(float s)
        {
            float a = BodyCamMagCheck.Alpha;
            string text = BodyCamMagCheck.Text;

            if (a < 0.01f || !BodyCamMagCheck.HasRect || string.IsNullOrEmpty(text))
                return;

            string ammo = "";
            float size = MagSize * s;
            float ammoSize = AmmoSize * s;

            float w1 = TextWidth(text, size);
            float w2 = ammo.Length > 0 ? TextWidth(ammo, ammoSize) : 0f;
            float h1 = LineHeight(size);
            float h2 = ammo.Length > 0 ? LineHeight(ammoSize) : 0f;

            float padX = 7f * s;
            float padY = 4f * s;
            float w = Mathf.Max(w1, w2) + padX * 2f;
            float h = h1 + h2 + padY * 2f;

            Rect m = BodyCamMagCheck.MagRect;

            Vector2 anchor = BodyCamLens.PictureToOverlay(
                new Vector2(m.xMax, m.yMin));
            Vector2 anchorLeft = BodyCamLens.PictureToOverlay(
                new Vector2(m.xMin, m.yMin));

            float gap = 3f * s;
            float x = anchor.x + gap;
            float y = anchor.y;

            if (x + w > Screen.width - 4f)
                x = anchorLeft.x - gap - w;

            x = Mathf.Clamp(x, 4f, Screen.width - w - 4f);
            y = Mathf.Clamp(y, 4f, Screen.height - h - 4f);

            Rect r = new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Round(w), Mathf.Round(h));
            Color c = BodyCamMagCheck.IsEmpty ? Red : (BodyCamMagCheck.IsLow ? Orange : Green);

            GUI.color = new Color(0f, 0f, 0f, 0.5f * a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            DrawFrame(r, Mathf.Max(1f, Mathf.Round(s)), new Color(c.r, c.g, c.b, 0.85f * a));

            DrawVhs(text, r.x + (r.width - w1) * 0.5f, r.y + padY, size, c, a);

            if (ammo.Length > 0)
                DrawVhs(ammo, r.x + (r.width - w2) * 0.5f, r.y + padY + h1, ammoSize, c, a);
        }

        private static void DrawFrame(Rect r, float t, Color c)
        {
            GUI.color = c;
            Texture2D white = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), white);
            GUI.DrawTexture(new Rect(r.x, r.y + t, t, r.height - 2f * t), white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), white);
        }

        private static Vector2 Place(OverlayCorner corner, float w, float h, float s, float screen)
        {
            int c = (int)corner;
            float used = CornerUsed[c];
            CornerUsed[c] = used + h + 10f * s;

            bool right = corner == OverlayCorner.TopRight || corner == OverlayCorner.BottomRight;
            bool bottom = corner == OverlayCorner.BottomLeft || corner == OverlayCorner.BottomRight;

            float mx = MarginX * screen;
            float my = MarginY * screen + (bottom ? 0f : TopCurveShift(screen));

            float x = right ? Screen.width - mx - w : mx;
            float y = bottom ? Screen.height - my - used - h : my + used;

            return new Vector2(x, y);
        }

        private static float TopCurveShift(float screen)
        {
            if (CfgCurve == null || !BodyCamLens.IsActive())
                return 0f;

            return 45f * Mathf.Clamp01(CfgCurve.Value) * screen;
        }

        private static int BatteryLevel(float now)
        {
            float minutes = CfgBatteryDrain.Value;

            if (minutes <= 0.01f)
                return BatteryBars;

            int used = Mathf.FloorToInt((now - _batteryStart) / (minutes * 60f));
            return Mathf.Clamp(BatteryBars - used, 1, BatteryBars);
        }

        private static Texture2D _batFrame;
        private static readonly Texture2D[] _batBars = new Texture2D[BatteryBars + 1];
        private static int _batW, _batH;

        private static void DrawBattery(Rect r, int bars)
        {
            int w = Mathf.Max(8, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(4, Mathf.RoundToInt(r.height));

            if (_batFrame == null || w != _batW || h != _batH)
                BuildBattery(w, h);

            r = new Rect(Mathf.Round(r.x), Mathf.Round(r.y), w, h);
            float o = Mathf.Max(1f, Mathf.Round(h / 9f));
            float f = Mathf.Max(1f, Mathf.Round(h / 20f));
            Texture2D barTex = _batBars[Mathf.Clamp(bars, 0, BatteryBars)];

            DrawTexLayers(r, o, f, _batFrame, Text);
            DrawTexLayers(r, o, f, barTex, bars <= 1 ? Red : Text);
        }

        private static void DrawTexLayers(Rect r, float o, float f, Texture2D tex, Color c)
        {
            if (tex == null)
                return;

            GUI.color = Shadow;
            GUI.DrawTexture(new Rect(r.x + o, r.y + o, r.width, r.height), tex);
            GUI.color = Pink;
            GUI.DrawTexture(new Rect(r.x - f, r.y, r.width, r.height), tex);
            GUI.color = Cyan;
            GUI.DrawTexture(new Rect(r.x + f, r.y, r.width, r.height), tex);
            GUI.color = c;
            GUI.DrawTexture(r, tex);
        }

        private static void BuildBattery(int w, int h)
        {
            _batW = w;
            _batH = h;

            float t = Mathf.Max(1.4f, h * 0.13f);
            float nubW = Mathf.Max(1.5f, h * 0.16f);
            float bodyW = w - nubW;
            float rad = h * 0.30f;

            Vector2 bc = new Vector2(bodyW * 0.5f, h * 0.5f);
            Vector2 bh = new Vector2(bodyW * 0.5f - 0.5f, h * 0.5f - 0.5f);

            Vector2 nc = new Vector2(bodyW + nubW * 0.5f - 0.5f, h * 0.5f);
            Vector2 nh = new Vector2(nubW * 0.5f, h * 0.21f);

            var frame = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float outer = Coverage(RoundBox(p - bc, bh, rad));
                    float inner = Coverage(RoundBox(p - bc, bh - new Vector2(t, t), Mathf.Max(0.5f, rad - t)));
                    float ring = Mathf.Clamp01(outer - inner);
                    float nub = Coverage(RoundBox(p - nc, nh, nubW * 0.45f));
                    float a = Mathf.Max(ring, nub * (1f - inner));
                    frame[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            _batFrame = MakeTex(w, h, frame, _batFrame);

            float pad = t + Mathf.Max(1f, h * 0.09f);
            float innerW = bodyW - 1f - 2f * pad;
            float innerH = h - 1f - 2f * pad;
            float barGap = Mathf.Max(1f, h * 0.08f);
            float barW = (innerW - barGap * (BatteryBars - 1)) / BatteryBars;
            float barRad = Mathf.Min(barW, innerH) * 0.3f;

            for (int n = 0; n <= BatteryBars; n++)
            {
                var px = new Color32[w * h];

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        float a = 0f;

                        for (int i = 0; i < n; i++)
                        {
                            Vector2 c = new Vector2(0.5f + pad + i * (barW + barGap) + barW * 0.5f, h * 0.5f);
                            a = Mathf.Max(a, Coverage(RoundBox(p - c, new Vector2(barW * 0.5f, innerH * 0.5f), barRad)));
                        }

                        px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }

                _batBars[n] = MakeTex(w, h, px, _batBars[n]);
            }
        }

        private static float RoundBox(Vector2 p, Vector2 half, float r)
        {
            r = Mathf.Min(r, Mathf.Min(half.x, half.y));
            Vector2 q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            Vector2 qm = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return qm.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Coverage(float d)
        {
            return Mathf.Clamp01(0.5f - d);
        }

        private static Texture2D MakeTex(int w, int h, Color32[] px, Texture2D old)
        {
            if (old != null)
                UnityEngine.Object.Destroy(old);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static GUIStyle _fallback;
        private static readonly GUIContent Content = new GUIContent();

        private static GUIStyle Fallback(float size)
        {
            if (_fallback == null)
            {
                _fallback = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    richText = false
                };
                _fallback.normal.textColor = Color.white;
            }

            _fallback.fontSize = Mathf.Max(6, Mathf.RoundToInt(size));
            return _fallback;
        }

        private static float TextWidth(string text, float size)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            if (BodyCamFont.Ready)
                return BodyCamFont.Width(text, size);

            Content.text = text;
            return Fallback(size).CalcSize(Content).x;
        }

        private static float LineHeight(float size)
        {
            return BodyCamFont.Ready ? BodyCamFont.LineHeight(size) : size * 1.2f;
        }

        private static float CapHeight(float size)
        {
            return BodyCamFont.Ready ? BodyCamFont.CapHeight(size) : size * 0.72f;
        }

        private static float Ascent(float size)
        {
            return BodyCamFont.Ready ? BodyCamFontData.Ascent * size / BodyCamFontData.RenderSize : size * 0.95f;
        }

        private static void DrawVhs(string text, float x, float y, float size, Color color, float alpha)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (BodyCamFont.Ready)
            {
                BodyCamFont.DrawVhs(text, x, y, size, color, alpha);
                return;
            }

            GUIStyle st = Fallback(size);
            Content.text = text;
            Vector2 sz = st.CalcSize(Content);
            float o = Mathf.Max(1f, size / 14f);

            GUI.color = new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * alpha);
            GUI.Label(new Rect(x + o, y + o, sz.x + 4f, sz.y), text, st);
            GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.Label(new Rect(x, y, sz.x + 4f, sz.y), text, st);
        }

        private static void UpdateClock(GameWorld world)
        {
            DateTime t = DateTime.Now;

            if (CfgRaidTime.Value)
            {
                DateTime raid;

                if (TryRaidTime(world, out raid))
                    t = DateTime.Now.Date + raid.TimeOfDay;
            }

            if (t.Second == _clockSecond && _clockText.Length > 0)
                return;

            _clockSecond = t.Second;
            _clockTime = t;

            _clockText = t.Year + ":" + t.Month + ":" + t.Day + "  " + t.ToString("HH:mm:ss");
        }

        private static bool TryRaidTime(GameWorld world, out DateTime time)
        {
            time = DateTime.MinValue;

            try
            {
                if (!_timeLookup)
                {
                    _timeLookup = true;

                    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _gameDateTimeField = typeof(GameWorld).GetField("GameDateTime", all);

                    if (_gameDateTimeField != null)
                        _calculate = _gameDateTimeField.FieldType.GetMethod("Calculate", all, null, Type.EmptyTypes, null);

                    if (_calculate == null)
                        Plugin.Log.LogWarning("[BodyCamera] Raid time not found, the overlay clock uses PC time.");
                }

                if (_calculate == null)
                    return false;

                object gdt = _gameDateTimeField.GetValue(world);

                if (gdt == null)
                    return false;

                time = (DateTime)_calculate.Invoke(gdt, null);
                return true;
            }
            catch
            {
                _calculate = null;
                return false;
            }
        }

        private static void Init()
        {
            if (_dot != null)
                return;

            const int n = 64;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false);
            _dot.hideFlags = HideFlags.HideAndDontSave;
            float r = n * 0.5f;

            for (int yy = 0; yy < n; yy++)
                for (int xx = 0; xx < n; xx++)
                {
                    float d = Vector2.Distance(new Vector2(xx + 0.5f, yy + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);
                    _dot.SetPixel(xx, yy, new Color(1f, 1f, 1f, a));
                }

            _dot.Apply();
        }
    }
}

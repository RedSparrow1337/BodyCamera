using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamLens
    {
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgFisheye;
        internal static ConfigEntry<float> CfgFisheyeV;
        internal static ConfigEntry<float> CfgRadialBlur;
        internal static ConfigEntry<float> CfgVignette;
        internal static ConfigEntry<float> CfgTint;
        internal static ConfigEntry<float> CfgEdge;
        internal static ConfigEntry<float> CfgProportion;
        internal static ConfigEntry<float> CfgMove;
        internal static ConfigEntry<float> CfgStepMove;
        internal static ConfigEntry<float> CfgDirt;
        internal static ConfigEntry<float> CfgDirtScale;
        internal static ConfigEntry<bool> CfgFlipFrame;
        internal static ConfigEntry<bool> CfgFlipOverlay;

        private const int GridX = 32;
        private const int GridY = 18;
        private const float FisheyeMax = 0.35f;
        private const float MoveScale = 0.0016f;
        private const float MoveMax = 0.09f;
        private const float MoveSmooth = 0.12f;

        private const float StepUp = 0.012f;
        private const float StepSide = 0.005f;
        private const float StepFreq = 3f;
        private const float StepDamping = 0.45f;

        private const float RimStart = -0.065f;

        private static readonly float[] RimOffset =
        {
            -0.045f, -0.030f, -0.015f, 0f, 0.015f, 0.030f
        };

        private static readonly Color[] RimColor =
        {
            new Color(0.86f, 0.81f, 0.72f),
            new Color(0.80f, 0.68f, 0.42f),
            new Color(0.74f, 0.44f, 0.15f),
            new Color(0.36f, 0.13f, 0.03f),
            new Color(0.08f, 0.02f, 0.00f),
            new Color(0.00f, 0.00f, 0.00f)
        };

        private const int RingSegments = 192;

        private static RenderTexture _frame;
        private static RenderTexture _overlay;

        private static Material _matTex;
        private static Material _matOverlay;
        private static bool _chromaFailed;
        private static Material _matColor;
        private static Material _matAlphaFix;
        private static Material _matDirt;
        private static Texture2D[] _dirtTex;
        private static bool _dirtFailed;
        private static bool _shadersFailed;

        private static int _overlayFrame = -1;

        private static RenderTexture _menu;
        private static int _menuFrame = -1;

        private static readonly RenderTexture[] _blur = new RenderTexture[6];
        private static int _blurFrame = -1;

        internal static Texture MenuBlur
        {
            get { return Time.frameCount - _blurFrame <= 2 ? _blur[5] : null; }
        }

        private static Quaternion _lastRot;
        private static bool _hasLastRot;
        private static Vector2 _turnVel;
        private static Vector2 _offset;
        private static Vector2 _turnOffset;
        private static Vector2 _stepOffset;
        private static Vector2 _stepVel;
        private static float _stepSide = 1f;

        private static Vector2[] _pos;
        private static Vector2[] _uv;
        private static float _cachedK = -1f;
        private static float _cachedAspect = -1f;
        private static bool _cachedFlip;

        internal static void BindConfig()
        {
            CfgEnabled = ConfigMigration.Bind(
                Plugin.SecLens, "Lens Effects", true,
                "Lens look: fisheye, vignette, dirt. Raid only.",
                null, 100, null, null);

            CfgFisheye = ConfigMigration.Bind(
                Plugin.SecLens, "Fisheye Horizontal", 1.00f,
                "Vertical lines bow out, more to the top and bottom (0 = off).",
                new AcceptableValueRange<float>(0f, 2f), 90, Plugin.SecLens, "Fisheye Strength");

            CfgFisheyeV = ConfigMigration.Bind(
                Plugin.SecLens, "Fisheye Vertical", 0.00f,
                "Horizontal lines bow out, more to the left and right (0 = off).",
                new AcceptableValueRange<float>(0f, 2f), 89, null, null);

            CfgRadialBlur = ConfigMigration.Bind(
                Plugin.SecLens, "Radial Blur", 1.00f,
                "The picture smears around the centre towards the edges (0 = off).",
                new AcceptableValueRange<float>(0f, 1f), 88, null, null);

            CfgVignette = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Strength", 0.72f,
                "Size of the image circle: higher = black reaches further in.",
                new AcceptableValueRange<float>(0f, 1f), 80, null, null);

            CfgProportion = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Proportion", 0.65f,
                "Shape of the image circle: 0 = round, 1 = screen proportions (oval).",
                new AcceptableValueRange<float>(0f, 1f), 79, null, null);

            CfgTint = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Tint", 1.00f,
                "Orange rim at the edge of the image circle.",
                new AcceptableValueRange<float>(0f, 1f), 75, null, null);

            CfgEdge = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Edge Width", 0.25f,
                "Width of the soft edge of the image circle (1 = old).",
                new AcceptableValueRange<float>(0.1f, 1f), 74, null, null);

            CfgMove = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Movement", 1.00f,
                "Image circle slides on fast turns.",
                new AcceptableValueRange<float>(0f, 2f), 70, null, null);

            CfgStepMove = ConfigMigration.Bind(
                Plugin.SecLens, "Vignette Step Movement", 2.00f,
                "Image circle bounces on every footstep.",
                new AcceptableValueRange<float>(0f, 2f), 68, null, null);

            CfgDirt = ConfigMigration.Bind(
                Plugin.SecLens, "Lens Dirt", 0.04f,
                "Dust and smudges on the lens glass (0 = off).",
                new AcceptableValueRange<float>(0f, 1f), 62, null, null);

            CfgDirtScale = ConfigMigration.Bind(
                Plugin.SecLens, "Lens Dirt Scale", 1.05f,
                "Size of the dust and smudges (1 = picture fits the screen, higher = bigger).",
                new AcceptableValueRange<float>(0.5f, 3f), 61, null, null);
        }

        internal static void BindDebugConfig()
        {
            CfgFlipFrame = ConfigMigration.Bind(
                Plugin.SecDebug, "Lens Flip Picture", false,
                "Turn on only if the picture is upside down.",
                null, 50, "9. Debug", "Lens Flip Picture");

            CfgFlipOverlay = ConfigMigration.Bind(
                Plugin.SecDebug, "Lens Flip Overlay", false,
                "Turn on only if the overlay text is upside down.",
                null, 40, "9. Debug", "Lens Flip Overlay");
        }

        internal static bool IsActive()
        {
            if (_shadersFailed || CfgEnabled == null || !CfgEnabled.Value ||
                Plugin.Enabled == null || !Plugin.Enabled.Value)
                return false;

            var world = Singleton<GameWorld>.Instance;

            if (world == null || Plugin.StartedWorld != world)
                return false;

            var player = world.MainPlayer;

            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return false;

            return !Cursor.visible || BodyCamMenu.Open;
        }

        internal static void BeginMenu()
        {
            Ensure(ref _menu, Screen.width, Screen.height);
            RenderTexture.active = _menu;
            GL.Clear(false, true, Color.clear);
            _menuFrame = Time.frameCount;
        }

        internal static RenderTexture BeginOverlay()
        {
            Ensure(ref _overlay, Screen.width, Screen.height);
            RenderTexture.active = _overlay;
            GL.Clear(false, true, Color.clear);
            _overlayFrame = Time.frameCount;
            return _overlay;
        }

        internal static IEnumerator Loop()
        {
            var wait = new WaitForEndOfFrame();

            while (true)
            {
                yield return wait;

                try
                {
                    OnEndOfFrame();
                }
                catch (Exception ex)
                {
                    if (Time.frameCount % 600 == 0)
                        Plugin.Log.LogError("[BodyCamera] Lens error: " + ex);
                }
            }
        }

        private static void OnEndOfFrame()
        {
            TrackTurn();

            if (!IsActive())
                return;

            if (!EnsureMaterials())
                return;

            int w = Screen.width, h = Screen.height;

            if (w < 16 || h < 16)
                return;

            Ensure(ref _frame, w, h);

            RenderTexture prev = RenderTexture.active;

            ScreenCapture.CaptureScreenshotIntoRenderTexture(_frame);

            RenderTexture.active = _frame;
            GL.PushMatrix();
            GL.LoadOrtho();
            _matAlphaFix.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0f, 0f, 0f, 1f));
            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(0f, 1f, 0f);
            GL.Vertex3(1f, 1f, 0f);
            GL.Vertex3(1f, 0f, 0f);
            GL.End();
            GL.PopMatrix();

            if (BodyCamMenu.Open)
                UpdateMenuBlur(w, h, SystemInfo.graphicsUVStartsAtTop ^ (CfgFlipFrame != null && CfgFlipFrame.Value));

            RenderTexture.active = null;

            float aspect = (float)w / h;
            bool flip = SystemInfo.graphicsUVStartsAtTop ^ (CfgFlipFrame != null && CfgFlipFrame.Value);
            float k = FisheyeH();

            BuildGrid(k, aspect, flip);

            GL.PushMatrix();
            GL.LoadOrtho();

            _matTex.mainTexture = _frame;

            if (_blurUv != null && _blurUv.Length > 0)
                DrawRadialBlur();
            else
                DrawChannel(UnityEngine.Rendering.ColorWriteMask.All, _uv);

            float dirt = CfgDirt != null ? Mathf.Clamp01(CfgDirt.Value) : 0f;

            if (dirt > 0.001f)
                DrawDirt(dirt);

            float vig = Mathf.Clamp01(CfgVignette.Value);

            if (vig > 0.001f && !BodyCamGunCam.Active)
            {
                _matColor.SetPass(0);
                DrawVignette(vig, Mathf.Clamp01(CfgTint.Value), aspect);
            }

            if (_overlayFrame == Time.frameCount && _overlay != null)
            {
                bool oflip = CfgFlipOverlay != null && CfgFlipOverlay.Value;
                _matOverlay.mainTexture = _overlay;
                _matOverlay.SetPass(0);

                float ko = OverlayK();

                if (ko > 0.0001f)
                {
                    BuildOverlayGrid(ko, aspect, oflip);
                    DrawGrid(_uvO);
                }
                else
                {
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord2(0f, oflip ? 1f : 0f); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(0f, oflip ? 0f : 1f); GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(1f, oflip ? 0f : 1f); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(1f, oflip ? 1f : 0f); GL.Vertex3(1f, 0f, 0f);
                GL.End();
                }
            }

            if (_menuFrame == Time.frameCount && _menu != null)
            {
                bool mflip = CfgFlipOverlay != null && CfgFlipOverlay.Value;
                _matOverlay.mainTexture = _menu;
                _matOverlay.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord2(0f, mflip ? 1f : 0f); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(0f, mflip ? 0f : 1f); GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(1f, mflip ? 0f : 1f); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(1f, mflip ? 1f : 0f); GL.Vertex3(1f, 0f, 0f);
                GL.End();
            }

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private static void UpdateMenuBlur(int w, int h, bool flip)
        {
            Rect r = BodyCamMenu.PanelPixels;

            if (r.width < 16f || r.height < 16f)
                return;

            int pw = Mathf.Max(8, (int)r.width / 2);
            int ph = Mathf.Max(8, (int)r.height / 2);
            int[] div = { 1, 2, 4, 8, 4, 2 };

            for (int i = 0; i < _blur.Length; i++)
                Ensure(ref _blur[i], Mathf.Max(2, pw / div[i]), Mathf.Max(2, ph / div[i]));

            float sx = r.width / w, sy = r.height / h, ox = r.x / w;
            Vector2 scale = flip ? new Vector2(sx, -sy) : new Vector2(sx, sy);
            Vector2 offset = flip
                ? new Vector2(ox, (r.y + r.height) / h)
                : new Vector2(ox, 1f - (r.y + r.height) / h);

            Graphics.Blit(_frame, _blur[0], scale, offset);

            for (int i = 1; i < _blur.Length; i++)
                Graphics.Blit(_blur[i - 1], _blur[i]);

            _blurFrame = Time.frameCount;
        }

        private static void TrackTurn()
        {
            Camera cam = Camera.main;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.1f);

            if (cam == null)
            {
                _hasLastRot = false;
                return;
            }

            Quaternion rot = cam.transform.rotation;

            if (_hasLastRot)
            {
                Quaternion d = Quaternion.Inverse(_lastRot) * rot;
                Vector3 e = d.eulerAngles;
                float yaw = e.y > 180f ? e.y - 360f : e.y;
                float pitch = e.x > 180f ? e.x - 360f : e.x;

                Vector2 vel = new Vector2(yaw, pitch) / dt;

                if (vel.magnitude > 2000f)
                    vel = Vector2.zero;

                _turnVel = Vector2.Lerp(_turnVel, vel, 1f - Mathf.Exp(-dt / MoveSmooth));
            }

            _lastRot = rot;
            _hasLastRot = true;

            float move = CfgMove != null ? Mathf.Max(0f, CfgMove.Value) : 0f;

            Vector2 target = new Vector2(-_turnVel.x, _turnVel.y) * MoveScale * move;
            target = Vector2.ClampMagnitude(target, MoveMax);
            _turnOffset = Vector2.Lerp(_turnOffset, target, 1f - Mathf.Exp(-dt / MoveSmooth));

            TickStep(dt);

            _offset = _turnOffset + _stepOffset;
        }

        internal static void OnFootstep()
        {
            float k = CfgStepMove != null ? Mathf.Max(0f, CfgStepMove.Value) : 0f;

            if (k < 0.0001f || !IsActive())
                return;

            _stepSide = -_stepSide;

            float impulse = ImpulseForPeak(StepFreq, StepDamping);
            _stepVel += new Vector2(_stepSide * StepSide, StepUp) * impulse * k;
        }

        private static void TickStep(float dt)
        {
            if (_stepOffset == Vector2.zero && _stepVel == Vector2.zero)
                return;

            float omega = 2f * Mathf.PI * StepFreq;
            float kk = omega * omega;
            float c = 2f * StepDamping * omega;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 25);
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                _stepVel += (-kk * _stepOffset - c * _stepVel) * h;
                _stepOffset += _stepVel * h;
            }

            if (float.IsNaN(_stepOffset.x) || float.IsNaN(_stepOffset.y) ||
                (_stepOffset.sqrMagnitude < 1e-10f && _stepVel.sqrMagnitude < 1e-8f))
            {
                _stepOffset = Vector2.zero;
                _stepVel = Vector2.zero;
            }

            _stepOffset = Vector2.ClampMagnitude(_stepOffset, MoveMax);
        }

        private static float ImpulseForPeak(float freq, float damping)
        {
            float z = Mathf.Clamp(damping, 0.05f, 0.95f);
            float omega = 2f * Mathf.PI * freq;
            float omegaD = omega * Mathf.Sqrt(1f - z * z);
            float tPeak = Mathf.Atan2(omegaD, z * omega) / omegaD;

            return omega * Mathf.Exp(z * omega * tPeak);
        }

        private const float LensZoomH = 0.5f / 0.55f;
        private const float LensDistH = 0.30f;
        private const float LensCubicH = 0.30f;

        private static float FisheyeH()
        {
            return CfgFisheye != null ? Mathf.Clamp(CfgFisheye.Value, 0f, 2f) : 0f;
        }

        private static float FisheyeV()
        {
            return CfgFisheyeV != null ? Mathf.Clamp(CfgFisheyeV.Value, 0f, 2f) : 0f;
        }

        private static Vector2 LensSource(Vector2 p, float kh, float kv)
        {
            if (kv > 0.0001f)
            {
                float x = p.x - 0.5f;
                float f = 1f + x * x * (LensDistH + LensCubicH * Mathf.Abs(x));
                float s = Mathf.LerpUnclamped(1f, LensZoomH * f, kv);
                p = new Vector2(0.5f + x * s, 0.5f + (p.y - 0.5f) * s);
            }

            if (kh > 0.0001f)
            {
                float y = p.y - 0.5f;
                float f = 1f + y * y * (LensDistH + LensCubicH * Mathf.Abs(y));
                float s = Mathf.LerpUnclamped(1f, LensZoomH * f, kh);
                p = new Vector2(0.5f + (p.x - 0.5f) * s, 0.5f + y * s);
            }

            return p;
        }

        private const int BlurCopiesPerSide = 6;
        private const float BlurFalloff = 1f + 4f * 0.6f;
        private static Vector2[][] _blurUv;
        private static float[] _blurAlpha;
        private static float _cachedKv = -1f;
        private static float _cachedBlur = -1f;

        private static float RadialBlurValue()
        {
            return CfgRadialBlur != null ? Mathf.Clamp01(CfgRadialBlur.Value) : 0f;
        }

        private static Vector2 BlurTurn(Vector2 uv, float angle, float aspect)
        {
            Vector2 q = new Vector2(uv.x - 0.5f, (uv.y - 0.5f) / aspect);
            float sn = Mathf.Sin(angle), cs = Mathf.Cos(angle);
            Vector2 r = new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
            return new Vector2(0.5f + r.x, 0.5f + r.y * aspect);
        }

        private static void BuildBlur(Vector2[] baseUv, float aspect, bool flip)
        {
            float b = RadialBlurValue();

            if (b <= 0.0001f)
            {
                _blurUv = null;
                return;
            }

            float strength = 0.02f * b;
            float quality = Mathf.Clamp(Mathf.Lerp(5f, 100f, strength * 50f) / BlurFalloff, 5f, 300f);
            int q = Mathf.Max(1, Mathf.CeilToInt(quality));
            int m = Mathf.Min(BlurCopiesPerSide, q);

            _blurUv = new Vector2[m * 2][];
            _blurAlpha = new float[m * 2];
            float sum = 0f;
            int n = 0;

            for (int c = 0; c < m; c++)
            {
                float i = m > 1 ? c * (q - 1f) / (m - 1f) : 0f;
                float baseAngle = (0.1f + i * 0.5f) * strength;
                float weight = 1f - Mathf.Abs(i / quality);

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var uv = new Vector2[baseUv.Length];

                    for (int v = 0; v < baseUv.Length; v++)
                    {
                        Vector2 src = baseUv[v];
                        float dx = src.x - 0.5f, dy = (src.y - 0.5f) / aspect;
                        float ff = Mathf.Pow(Mathf.Sqrt(dx * dx + dy * dy), BlurFalloff);
                        Vector2 t = BlurTurn(src, sign * baseAngle * ff, aspect);

                        if (flip)
                            t.y = 1f - t.y;

                        uv[v] = t;
                    }

                    sum += weight;
                    _blurUv[n] = uv;
                    _blurAlpha[n] = weight / Mathf.Max(sum, 0.0001f);
                    n++;
                }
            }
        }

        private static void DrawRadialBlur()
        {
            _matTex.SetFloat("_ColorMask", (float)(int)UnityEngine.Rendering.ColorWriteMask.All);
            _matTex.SetPass(0);

            for (int i = 0; i < _blurUv.Length; i++)
                DrawGrid(_blurUv[i], i == 0 ? 1f : _blurAlpha[i]);
        }

        private static void BuildGrid(float k, float aspect, bool flip)
        {
            float kv = FisheyeV();
            float blur = RadialBlurValue();

            if (_pos != null &&
                Mathf.Abs(kv - _cachedKv) < 0.0001f &&
                Mathf.Abs(blur - _cachedBlur) < 0.0001f &&
                Mathf.Abs(k - _cachedK) < 0.0001f &&
                Mathf.Abs(aspect - _cachedAspect) < 0.0001f &&
                flip == _cachedFlip)
                return;

            _cachedK = k;
            _cachedKv = kv;
            _cachedBlur = blur;
            _cachedAspect = aspect;
            _cachedFlip = flip;

            int n = (GridX + 1) * (GridY + 1);
            _pos = new Vector2[n];
            _uv = new Vector2[n];
            var baseUv = new Vector2[n];

            for (int y = 0; y <= GridY; y++)
            {
                for (int x = 0; x <= GridX; x++)
                {
                    int i = y * (GridX + 1) + x;
                    Vector2 p = new Vector2((float)x / GridX, (float)y / GridY);
                    _pos[i] = p;

                    Vector2 src = LensSource(p, k, kv);
                    baseUv[i] = src;

                    if (flip)
                        src.y = 1f - src.y;

                    _uv[i] = src;
                }
            }

            BuildBlur(baseUv, aspect, flip);
        }

        private static Vector2[] _uvO;
        private static float _cachedKo = -1f;
        private static float _cachedAspectO = -1f;
        private static bool _cachedFlipO;

        private static float OverlayK()
        {
            var cfg = BodyCamOverlay.CfgCurve;
            return cfg != null ? FisheyeMax * Mathf.Clamp01(cfg.Value) : 0f;
        }

        private static Vector2 Barrel(Vector2 p, float k, float aspect)
        {
            float rc = Mathf.Sqrt(0.25f * aspect * aspect + 0.25f);
            Vector2 q = new Vector2((p.x - 0.5f) * aspect, p.y - 0.5f);
            float r = q.magnitude / rc;
            float sc = 1f - k * (1f - r * r);
            return new Vector2(0.5f + q.x * sc / aspect, 0.5f + q.y * sc);
        }

        private static void BuildOverlayGrid(float k, float aspect, bool flip)
        {
            if (_uvO != null && _pos != null &&
                Mathf.Abs(k - _cachedKo) < 0.0001f &&
                Mathf.Abs(aspect - _cachedAspectO) < 0.0001f &&
                flip == _cachedFlipO)
                return;

            _cachedKo = k;
            _cachedAspectO = aspect;
            _cachedFlipO = flip;

            int n = (GridX + 1) * (GridY + 1);
            _uvO = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 src = Barrel(_pos[i], k, aspect);

                if (flip)
                    src.y = 1f - src.y;

                _uvO[i] = src;
            }
        }

        internal static Vector2 PictureToOverlay(Vector2 gui)
        {
            if (!IsActive())
                return gui;

            float w = Screen.width, h = Screen.height;

            if (w < 16f || h < 16f)
                return gui;

            float aspect = w / h;
            float kp = FisheyeH();
            float kpv = FisheyeV();
            float ko = OverlayK();
            Vector2 m = new Vector2(gui.x / w, gui.y / h);

            Vector2 p = m;

            if (kp > 0.0001f || kpv > 0.0001f)
                for (int i = 0; i < 10; i++)
                    p += m - LensSource(p, kp, kpv);

            Vector2 o = ko > 0.0001f ? Barrel(p, ko, aspect) : p;
            return new Vector2(o.x * w, o.y * h);
        }

        private static void DrawChannel(UnityEngine.Rendering.ColorWriteMask mask, Vector2[] uv)
        {
            _matTex.SetFloat("_ColorMask", (float)(int)mask);
            _matTex.SetPass(0);
            DrawGrid(uv);
        }

        private static void DrawDirt(float strength)
        {
            if (_chromaFailed || !EnsureDirt())
                return;

            UnityEngine.Rendering.ColorWriteMask[] masks =
            {
                UnityEngine.Rendering.ColorWriteMask.Red,
                UnityEngine.Rendering.ColorWriteMask.Green,
                UnityEngine.Rendering.ColorWriteMask.Blue
            };

            float scale = CfgDirtScale != null ? Mathf.Clamp(CfgDirtScale.Value, 0.5f, 3f) : 1f;
            float u0 = 0.5f - 0.5f / scale;
            float u1 = 0.5f + 0.5f / scale;

            for (int c = 0; c < 3; c++)
            {
                _matDirt.mainTexture = _dirtTex[c];
                _matDirt.SetFloat("_ColorMask", (float)(int)masks[c]);
                _matDirt.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.Color(new Color(1f, 1f, 1f, strength));
                GL.TexCoord2(u0, u0); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(u0, u1); GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(u1, u1); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(u1, u0); GL.Vertex3(1f, 0f, 0f);
                GL.End();
            }
        }

        private static bool EnsureDirt()
        {
            if (_dirtTex != null)
                return true;

            if (_dirtFailed)
                return false;

            try
            {
                var asm = typeof(BodyCamLens).Assembly;
                byte[] png = null;

                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("LensDirt.png", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (Stream s = asm.GetManifestResourceStream(name))
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        png = ms.ToArray();
                    }

                    break;
                }

                if (png == null)
                    throw new InvalidOperationException("resource LensDirt.png not found");

                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!src.LoadImage(png))
                    throw new InvalidOperationException("LensDirt.png could not be decoded");

                int w = src.width, h = src.height;
                Color32[] px = src.GetPixels32();
                UnityEngine.Object.Destroy(src);

                var tex = new Texture2D[3];
                var data = new byte[w * h];

                for (int c = 0; c < 3; c++)
                {
                    for (int i = 0; i < data.Length; i++)
                        data[i] = c == 0 ? px[i].r : (c == 1 ? px[i].g : px[i].b);

                    var t = new Texture2D(w, h, TextureFormat.Alpha8, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Mirror,
                        hideFlags = HideFlags.HideAndDontSave,
                        name = "BodyCamDirt" + c
                    };

                    t.LoadRawTextureData(data);
                    t.Apply(false, true);
                    tex[c] = t;
                }

                Shader ui = Shader.Find("UI/Default");
                _matDirt = NewUi(ui);

                _matDirt.SetVector("_TextureSampleAdd", new Vector4(1f, 1f, 1f, 0f));

                _dirtTex = tex;
                Plugin.Log.LogInfo("[BodyCamera] Lens dirt ready (" + w + "x" + h + ").");
                return true;
            }
            catch (Exception ex)
            {
                _dirtFailed = true;
                Plugin.Log.LogWarning("[BodyCamera] Lens dirt disabled: " + ex.Message);
                return false;
            }
        }

        private static void DrawGrid(Vector2[] uv)
        {
            DrawGrid(uv, 1f);
        }

        private static void DrawGrid(Vector2[] uv, float alpha)
        {
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, alpha));

            for (int y = 0; y < GridY; y++)
            {
                for (int x = 0; x < GridX; x++)
                {
                    int i0 = y * (GridX + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + GridX + 2;
                    int i3 = i0 + GridX + 1;

                    Vertex(i0, uv);
                    Vertex(i3, uv);
                    Vertex(i2, uv);
                    Vertex(i1, uv);
                }
            }

            GL.End();
        }

        private static void Vertex(int i, Vector2[] uv)
        {
            GL.TexCoord2(uv[i].x, uv[i].y);
            GL.Vertex3(_pos[i].x, _pos[i].y, 0f);
        }

        private const int FineRings = 40;
        private static float[] _vigRadius;
        private static Color[] _vigColor;
        private static float _vigTint = -1f;
        private static float _vigEdge = -1f;
        private static float[] _segCos;
        private static float[] _segSin;

        private static void DrawVignette(float strength, float tint, float aspect)
        {
            float R = Mathf.Lerp(1.08f, 0.86f, strength);

            Vector2 c = new Vector2(0.5f + _offset.x / aspect, 0.5f + _offset.y);

            SetProportion(aspect);
            BuildVignette(R, tint);

            int rings = _vigRadius.Length;

            GL.Begin(GL.TRIANGLE_STRIP);

            for (int ring = 0; ring < rings - 1; ring++)
            {
                float r0 = _vigRadius[ring] * R;
                float r1 = _vigRadius[ring + 1] * R;

                Color c0 = _vigColor[ring], c1 = _vigColor[ring + 1];

                if (ring > 0)
                    RingVertex(c, aspect, 0, r0, c0);

                for (int sgm = 0; sgm <= RingSegments; sgm++)
                {
                    int k = sgm == RingSegments ? 0 : sgm;
                    RingVertex(c, aspect, k, r0, c0);
                    RingVertex(c, aspect, k, r1, c1);
                }

                if (ring < rings - 2)
                    RingVertex(c, aspect, 0, r1, c1);
            }

            GL.End();
        }

        private static float _vigR = -1f;

        private static void BuildVignette(float R, float tint)
        {
            if (_segCos == null)
            {
                _segCos = new float[RingSegments];
                _segSin = new float[RingSegments];

                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i * (Mathf.PI * 2f / RingSegments);
                    _segCos[i] = Mathf.Cos(a);
                    _segSin[i] = Mathf.Sin(a);
                }
            }

            float edge = CfgEdge != null ? Mathf.Clamp(CfgEdge.Value, 0.1f, 1f) : 1f;

            if (_vigRadius != null && Mathf.Abs(_vigTint - tint) < 0.0005f && Mathf.Abs(_vigR - R) < 0.0005f &&
                Mathf.Abs(_vigEdge - edge) < 0.0005f)
                return;

            _vigEdge = edge;
            _vigTint = tint;
            _vigR = R;

            int n = RimOffset.Length + 1;
            float[] x = new float[n];
            Color[] y = new Color[n];
            x[0] = R + RimStart * edge;
            y[0] = Color.white;

            for (int i = 0; i < RimOffset.Length; i++)
            {
                x[i + 1] = R + RimOffset[i] * edge;
                y[i + 1] = Tinted(RimColor[i], tint);
            }

            float[] mr = Slopes(x, y, 0);
            float[] mg = Slopes(x, y, 1);
            float[] mb = Slopes(x, y, 2);

            _vigRadius = new float[FineRings + 2];
            _vigColor = new Color[FineRings + 2];

            for (int i = 0; i <= FineRings; i++)
            {
                float r = Mathf.Lerp(x[0], x[n - 1], i / (float)FineRings);
                int seg = 0;

                while (seg < n - 2 && r > x[seg + 1])
                    seg++;

                float h = x[seg + 1] - x[seg];
                float t = Mathf.Clamp01((r - x[seg]) / h);

                _vigRadius[i] = r / R;
                _vigColor[i] = new Color(
                    Hermite(y[seg].r, y[seg + 1].r, mr[seg] * h, mr[seg + 1] * h, t),
                    Hermite(y[seg].g, y[seg + 1].g, mg[seg] * h, mg[seg + 1] * h, t),
                    Hermite(y[seg].b, y[seg + 1].b, mb[seg] * h, mb[seg + 1] * h, t),
                    1f);
            }

            _vigRadius[FineRings + 1] = 3f / R;
            _vigColor[FineRings + 1] = Color.black;
        }

        private static float[] Slopes(float[] x, Color[] y, int ch)
        {
            int n = x.Length;
            float[] d = new float[n - 1];
            float[] m = new float[n];

            for (int i = 0; i < n - 1; i++)
                d[i] = (y[i + 1][ch] - y[i][ch]) / (x[i + 1] - x[i]);

            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0f)
                {
                    m[i] = 0f;
                }
                else
                {
                    float w1 = 2f * (x[i + 1] - x[i]) + (x[i] - x[i - 1]);
                    float w2 = (x[i + 1] - x[i]) + 2f * (x[i] - x[i - 1]);
                    m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
                }
            }

            m[0] = 0f;
            m[n - 1] = 0f;
            return m;
        }

        private static float Hermite(float p0, float p1, float m0, float m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return Mathf.Clamp01(
                (2f * t3 - 3f * t2 + 1f) * p0 +
                (t3 - 2f * t2 + t) * m0 +
                (-2f * t3 + 3f * t2) * p1 +
                (t3 - t2) * m1);
        }

        private static void RingVertex(Vector2 c, float aspect, int seg, float r, Color color)
        {
            GL.Color(color);
            GL.Vertex3(
                c.x + _segCos[seg] * r * _vigSx / aspect,
                c.y + _segSin[seg] * r * _vigSy,
                0f);
        }

        private static float _vigSx = 1f;
        private static float _vigSy = 1f;

        private static void SetProportion(float aspect)
        {
            float p = CfgProportion != null ? Mathf.Clamp01(CfgProportion.Value) : 0f;
            float hw = 0.5f * aspect, hh = 0.5f;
            float d = Mathf.Sqrt(hw * hw + hh * hh);
            _vigSx = Mathf.Lerp(1f, hw * 1.41421356f / d, p);
            _vigSy = Mathf.Lerp(1f, hh * 1.41421356f / d, p);
        }

        private static Color Tinted(Color c, float tint)
        {
            float g = (c.r + c.g + c.b) / 3f;
            Color n = new Color(g, g, g);
            Color o = Color.Lerp(n, c, tint);
            o.a = 1f;
            return o;
        }

        private static void Ensure(ref RenderTexture rt, int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h && rt.IsCreated())
                return;

            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }

            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                name = "BodyCamLens"
            };

            rt.Create();
        }

        private static bool EnsureMaterials()
        {
            if (_matTex != null && _matColor != null)
                return true;

            Shader ui = Shader.Find("UI/Default");
            Shader colored = Shader.Find("Hidden/Internal-Colored");

            if (ui == null || colored == null)
            {
                _shadersFailed = true;
                Plugin.Log.LogWarning(
                    "[BodyCamera] Lens effects disabled: built-in shader not found (UI/Default=" +
                    (ui != null) + ", Internal-Colored=" + (colored != null) + ").");
                return false;
            }

            _matTex = NewUi(ui);
            _matOverlay = NewUi(ui);

            if (!_matTex.HasProperty("_ColorMask"))
            {
                _chromaFailed = true;
                Plugin.Log.LogWarning(
                    "[BodyCamera] Lens dirt disabled: UI/Default has no _ColorMask.");
            }

            _matColor = new Material(colored) { hideFlags = HideFlags.HideAndDontSave };

            _matColor.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.DstColor);
            _matColor.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _matColor.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _matColor.SetInt("_ZWrite", 0);
            _matColor.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

            _matAlphaFix = new Material(colored) { hideFlags = HideFlags.HideAndDontSave };
            _matAlphaFix.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _matAlphaFix.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _matAlphaFix.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _matAlphaFix.SetInt("_ZWrite", 0);
            _matAlphaFix.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

            Plugin.Log.LogInfo("[BodyCamera] Lens effects ready.");
            return true;
        }

        private static Material NewUi(Shader ui)
        {
            var m = new Material(ui) { hideFlags = HideFlags.HideAndDontSave };
            m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.SetVector("_TextureSampleAdd", Vector4.zero);
            m.color = Color.white;
            return m;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamFont
    {
        private struct Glyph
        {
            public Rect Uv;
            public float W, H, Ox, Oy, Adv;
        }

        private static Texture2D _tex;
        private static bool _failed;
        private static Dictionary<char, Glyph> _glyphs;
        private static Glyph _unknown;

        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

        private static readonly Color Pink = new Color(1f, 0.30f, 0.55f, 0.35f);
        private static readonly Color Cyan = new Color(0.30f, 0.85f, 1f, 0.35f);

        internal static bool Ready
        {
            get { return Load(); }
        }

        private static bool Load()
        {
            if (_tex != null)
                return true;

            if (_failed)
                return false;

            _failed = true;

            try
            {
                byte[] png = ReadResource("vcr_font.png");

                if (png == null)
                {
                    Plugin.Log.LogWarning("[BodyCamera] Overlay font not found in the DLL, Arial is used.");
                    return false;
                }

                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!ImageConversion.LoadImage(raw, png))
                    return false;

                var tex = new Texture2D(raw.width, raw.height, TextureFormat.RGBA32, true);
                tex.SetPixels32(raw.GetPixels32());
                tex.Apply(true, true);
                UnityEngine.Object.Destroy(raw);

                tex.filterMode = FilterMode.Trilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.mipMapBias = -0.6f;
                tex.hideFlags = HideFlags.HideAndDontSave;

                float[] d = BodyCamFontData.Glyphs;
                float aw = BodyCamFontData.AtlasWidth;
                float ah = BodyCamFontData.AtlasHeight;
                _glyphs = new Dictionary<char, Glyph>(d.Length / 8);

                for (int i = 0; i + 7 < d.Length; i += 8)
                {
                    var g = new Glyph
                    {
                        Uv = new Rect(d[i + 1] / aw, 1f - (d[i + 2] + d[i + 4]) / ah, d[i + 3] / aw, d[i + 4] / ah),
                        W = d[i + 3],
                        H = d[i + 4],
                        Ox = d[i + 5],
                        Oy = d[i + 6],
                        Adv = d[i + 7]
                    };

                    _glyphs[(char)(int)d[i]] = g;
                }

                _glyphs.TryGetValue('?', out _unknown);
                _tex = tex;
                _failed = false;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Overlay font error: " + ex.Message);
                return false;
            }
        }

        private static byte[] ReadResource(string file)
        {
            var asm = typeof(BodyCamFont).Assembly;

            foreach (string n in asm.GetManifestResourceNames())
            {
                if (!n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) &&
                    !n.Equals(file, StringComparison.OrdinalIgnoreCase))
                    continue;

                using (Stream s = asm.GetManifestResourceStream(n))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }

            return null;
        }

        private static Glyph Get(char c)
        {
            Glyph g;
            return _glyphs.TryGetValue(c, out g) ? g : _unknown;
        }

        internal static float Width(string text, float size)
        {
            if (string.IsNullOrEmpty(text) || !Load())
                return 0f;

            float k = size / BodyCamFontData.RenderSize;
            float w = 0f;

            for (int i = 0; i < text.Length; i++)
                w += Get(text[i]).Adv;

            return w * k;
        }

        internal static float LineHeight(float size)
        {
            return (BodyCamFontData.Ascent + BodyCamFontData.Descent) * size / BodyCamFontData.RenderSize;
        }

        internal static float CapHeight(float size)
        {
            return BodyCamFontData.CapHeight * size / BodyCamFontData.RenderSize;
        }

        internal static void Draw(string text, float x, float y, float size, Color color)
        {
            if (string.IsNullOrEmpty(text) || !Load())
                return;

            float k = size / BodyCamFontData.RenderSize;
            float baseline = y + BodyCamFontData.Ascent * k;
            GUI.color = color;

            for (int i = 0; i < text.Length; i++)
            {
                Glyph g = Get(text[i]);

                if (g.W > 0f && text[i] != ' ')
                    GUI.DrawTextureWithTexCoords(
                        new Rect(Mathf.Round(x + g.Ox * k), Mathf.Round(baseline + g.Oy * k),
                                 Mathf.Round(g.W * k), Mathf.Round(g.H * k)),
                        _tex, g.Uv, true);

                x += g.Adv * k;
            }
        }

        internal static void DrawVhs(string text, float x, float y, float size, Color color, float alpha)
        {
            if (alpha <= 0.001f)
                return;

            float o = Mathf.Max(1f, Mathf.Round(size / 18f));
            float f = Mathf.Max(1f, Mathf.Round(size / 30f));

            Draw(text, x + o, y + o, size, Fade(ShadowColor, alpha));
            Draw(text, x - f, y, size, Fade(Pink, alpha));
            Draw(text, x + f, y, size, Fade(Cyan, alpha));
            Draw(text, x, y, size, Fade(color, alpha));
        }

        private static Color Fade(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, c.a * a);
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using UnityEngine;

namespace BodyCamera
{
    internal static class BodyCamRecoilProbe
    {
        private const float TailTime = 0.8f;
        private const int MaxRows = 3000;

        private static readonly StringBuilder _buf = new StringBuilder(1 << 16);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static bool _capturing;
        private static float _start;
        private static float _lastShot;
        private static int _rows;
        private static int _shots;
        private static int _pendingShots;
        private static int _burst;
        private static int _lastFrame = -1;
        private static bool _headerWritten;
        private static bool _failed;

        private static FieldInfo _camRecoilField;
        private static bool _fieldLookup;

        private static bool Enabled
        {
            get { return !_failed && Plugin.DebugLogging != null && Plugin.DebugLogging.Value; }
        }

        private static string FilePath
        {
            get { return Path.Combine(BepInEx.Paths.BepInExRootPath, "BodyCamera_recoil_probe.csv"); }
        }

        internal static void OnShot(Player.FirearmController controller, Player player)
        {
            if (!Enabled)
                return;

            try
            {
                float now = Time.time;

                if (!_capturing)
                {
                    _capturing = true;
                    _start = now;
                    _rows = 0;
                    _shots = 0;
                    _burst++;
                    _buf.Length = 0;
                    _buf.Append("# burst ").Append(_burst).Append(", ")
                          .Append(DateTime.Now.ToString("HH:mm:ss", Inv)).Append(", ")
                          .Append(WeaponInfo(controller, player)).Append('\n');
                }

                _lastShot = now;
                _shots++;
                _pendingShots++;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        internal static void Sample(ProceduralWeaponAnimation pwa)
        {
            if (!_capturing)
                return;

            int frame = Time.frameCount;

            if (frame == _lastFrame)
                return;

            _lastFrame = frame;

            try
            {
                float now = Time.time;

                if (now - _lastShot > TailTime || _rows >= MaxRows || !Enabled)
                {
                    Flush();
                    return;
                }

                var effect = pwa.Shootingg != null ? pwa.Shootingg.CurrentRecoilEffect : null;
                var hc = pwa.HandsContainer;

                Vector3 hr = effect != null ? effect.GetHandRotationRecoil() : Vector3.zero;
                Vector3 hp = effect != null ? effect.GetHandPositionRecoil() : Vector3.zero;
                Vector3 cr = effect != null ? effect.GetCameraRotationRecoil() : Vector3.zero;

                Vector3 sHR = hc != null && hc.HandsRotation != null ? hc.HandsRotation.Current : Vector3.zero;
                Vector3 sHP = hc != null && hc.HandsPosition != null ? hc.HandsPosition.Current : Vector3.zero;
                Vector3 sCR = hc != null && hc.CameraRotation != null ? hc.CameraRotation.Current : Vector3.zero;
                Vector3 sCP = hc != null && hc.CameraPosition != null ? hc.CameraPosition.Current : Vector3.zero;

                Vector3 cam = Vector3.zero;
                FieldInfo f = CamRecoilField();

                if (f != null)
                {
                    Quaternion q = (Quaternion)f.GetValue(pwa);
                    cam = Signed(q.eulerAngles);
                }

                _buf.Append(((now - _start) * 1000f).ToString("0.0", Inv)).Append(',')
                      .Append((Time.deltaTime * 1000f).ToString("0.0", Inv)).Append(',')
                      .Append(_pendingShots).Append(',')
                      .Append(pwa.IsAiming ? 1 : 0);

                _pendingShots = 0;

                Add(hr);
                Add(hp);
                Add(cr);
                Add(sHR);
                Add(sHP);
                Add(sCR);
                Add(sCP);
                Add(cam);
                _buf.Append('\n');

                _rows++;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        internal static void Flush()
        {
            if (!_capturing)
                return;

            _capturing = false;

            try
            {
                var sb = new StringBuilder();

                if (!_headerWritten)
                {
                    _headerWritten = true;
                    sb.Append("# session ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv))
                      .Append(", BodyCamera 0.9.43\n");
                    sb.Append("t_ms,dt_ms,shot,aim,")
                      .Append("hr_x,hr_y,hr_z,hp_x,hp_y,hp_z,cr_x,cr_y,cr_z,")
                      .Append("sHR_x,sHR_y,sHR_z,sHP_x,sHP_y,sHP_z,")
                      .Append("sCR_x,sCR_y,sCR_z,sCP_x,sCP_y,sCP_z,")
                      .Append("cam_x,cam_y,cam_z\n");
                }

                sb.Append(_buf);
                File.AppendAllText(FilePath, sb.ToString());

                Plugin.Log.LogInfo(
                    "[BodyCamera] Recoil probe: burst " + _burst + ", " + _shots +
                    " shot(s), " + _rows + " frames -> " + FilePath);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            _buf.Length = 0;
            _rows = 0;
            _pendingShots = 0;
        }

        private static void Add(Vector3 v)
        {
            _buf.Append(',').Append(v.x.ToString("0.#####", Inv))
                  .Append(',').Append(v.y.ToString("0.#####", Inv))
                  .Append(',').Append(v.z.ToString("0.#####", Inv));
        }

        private static Vector3 Signed(Vector3 e)
        {
            return new Vector3(
                e.x > 180f ? e.x - 360f : e.x,
                e.y > 180f ? e.y - 360f : e.y,
                e.z > 180f ? e.z - 360f : e.z);
        }

        private static FieldInfo CamRecoilField()
        {
            if (!_fieldLookup)
            {
                _fieldLookup = true;
                _camRecoilField = typeof(ProceduralWeaponAnimation).GetField(
                    "_currentRecoilCameraRotate",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return _camRecoilField;
        }

        private static string WeaponInfo(Player.FirearmController controller, Player player)
        {
            var sb = new StringBuilder();

            Weapon w = controller != null ? controller.Weapon : null;

            if (w != null)
            {
                sb.Append("weapon ").Append(w.TemplateId.ToString())
                  .Append(" class ").Append(w.WeapClass)
                  .Append(" caliber ").Append(w.AmmoCaliber)
                  .Append(" mode ").Append(w.SelectedFireMode)
                  .Append(" recoil ").Append(w.RecoilBase.ToString("0.#", Inv))
                  .Append("->").Append(w.RecoilTotal.ToString("0.#", Inv))
                  .Append(" back ").Append(w.RecoilForceBack.ToString("0.#", Inv));
            }

            var pwa = player != null ? player.ProceduralWeaponAnimation : null;

            if (pwa != null)
            {
                sb.Append(" | camSmoothRecoil ").Append(pwa.CameraSmoothRecoil.ToString("0.###", Inv));

                var effect = pwa.Shootingg != null ? pwa.Shootingg.CurrentRecoilEffect : null;

                if (effect != null)
                {
                    sb.Append(" | effect ").Append(effect.GetType().Name);
                    Process(sb, "handRot", effect.HandRotationRecoilEffect);
                    Process(sb, "handPos", effect.HandPositionRecoilEffect);
                    Process(sb, "camRot", effect.CameraRotationRecoilEffect);
                }

                var hc = pwa.HandsContainer;

                if (hc != null)
                {
                    SpringInfo(sb, "sHandsRot", hc.HandsRotation);
                    SpringInfo(sb, "sHandsPos", hc.HandsPosition);
                    SpringInfo(sb, "sCamRot", hc.CameraRotation);
                    SpringInfo(sb, "sCamPos", hc.CameraPosition);
                    sb.Append(" | recoilPivot ").Append(V(hc.RecoilPivot))
                      .Append(" rotationCenter ").Append(V(hc.RotationCenter));
                }
            }

            return sb.ToString();
        }

        private static void Process(StringBuilder sb, string name, RecoilProcessBase p)
        {
            if (p == null)
                return;

            sb.Append(" | ").Append(name)
              .Append(" return ").Append(p.ReturnSpeed.ToString("0.###", Inv))
              .Append(" damping ").Append(p.Damping.ToString("0.###", Inv))
              .Append(" intensity ").Append(p.Intensity.ToString("0.###", Inv));
        }

        private static void SpringInfo(StringBuilder sb, string name, EFT.Animations.Spring s)
        {
            if (s == null)
                return;

            sb.Append(" | ").Append(name)
              .Append(" return ").Append(s.ReturnSpeed.ToString("0.###", Inv))
              .Append(" damping ").Append(s.Damping.ToString("0.###", Inv))
              .Append(" input ").Append(s.InputIntensity.ToString("0.###", Inv));
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("0.###", Inv) + " " +
                   v.y.ToString("0.###", Inv) + " " +
                   v.z.ToString("0.###", Inv) + ")";
        }

        private static void Fail(Exception ex)
        {
            if (_failed)
                return;

            _failed = true;
            _capturing = false;
            Plugin.Log.LogWarning("[BodyCamera] Recoil probe stopped: " + ex);
        }
    }
}

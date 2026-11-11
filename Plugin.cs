using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using SPT.Reflection.Patching;
using UnityEngine;

namespace BodyCamera
{
    internal enum DeadZoneMode
    {
        Off,
        Camera,
        Weapon
    }

    [BepInPlugin("BodyCamera", "BodyCamera", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<KeyCode> ThirdPersonKey;
        internal static ConfigEntry<float> ThirdPersonX;
        internal static ConfigEntry<float> ThirdPersonY;
        internal static ConfigEntry<float> ThirdPersonZ;
        internal static bool ThirdPerson;
        internal static ConfigEntry<KeyCode> ShoulderSwapKey;
        internal static ConfigEntry<float> ThirdPersonInertia;
        internal static ConfigEntry<float> ThirdPersonShake;
        internal static ConfigEntry<bool> ThirdPersonAdsChest;
        internal static ConfigEntry<bool> ThirdPersonVisionEffects;
        internal static bool ShoulderSwapped;

        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;
        internal static ConfigEntry<float> OffsetZ;
        internal static ConfigEntry<float> PistolOffsetX;
        internal static ConfigEntry<float> PistolOffsetY;
        internal static ConfigEntry<float> PistolOffsetZ;
        internal static ConfigEntry<float> HipAimOffsetUp;
        internal static ConfigEntry<float> HipAimOffsetSide;
        internal static ConfigEntry<float> HipAimOffsetRoll;

        internal static ConfigEntry<bool> KeepCameraRotation;
        internal static ConfigEntry<float> RotationInertia;
        internal static ConfigEntry<float> CameraDeadZone;
        internal static ConfigEntry<DeadZoneMode> DeadZoneModeCfg;
        internal static ConfigEntry<float> WeaponDeadZone;
        internal static ConfigEntry<float> LeanRollStrength;

        internal static ConfigEntry<float> CameraShakeMultiplier;
        internal static ConfigEntry<float> MotionSmoothing;
        internal static ConfigEntry<float> StepNod;
        internal static ConfigEntry<float> AdsStepNod;
        internal static ConfigEntry<float> RandomSway;
        internal static ConfigEntry<float> StepRollStrength;
        internal static ConfigEntry<float> TurnRoll;
        internal static ConfigEntry<float> LookShake;
        internal static ConfigEntry<float> CameraLookDownLimit;
        internal static ConfigEntry<float> BodyTiltStrength;
        internal static ConfigEntry<float> ShotReactionStrength;
        internal static ConfigEntry<float> ShotReaction2;
        internal static ConfigEntry<float> IdleShake;
        internal static ConfigEntry<bool> IdleShakeInMotion;
        internal static ConfigEntry<float> HitImpact;
        internal static ConfigEntry<float> ExplosionImpact;
        internal static ConfigEntry<float> FallImpact;

        internal static ConfigEntry<float> IdleMotionStrength;
        internal static ConfigEntry<float> WalkMotionStrength;
        internal static ConfigEntry<float> AdsIdleMotionStrength;
        internal static ConfigEntry<float> AdsWalkMotionStrength;
        internal static ConfigEntry<float> WalkSlow;
        internal static ConfigEntry<float> WalkNormal;
        internal static ConfigEntry<float> WalkFast;
        internal static ConfigEntry<float> WalkSprint;
        internal static ConfigEntry<float> AdsWalkSlow;
        internal static ConfigEntry<float> AdsWalkNormal;
        internal static ConfigEntry<float> AdsWalkFast;
        internal static ConfigEntry<float> WeaponMotionSpring;
        internal static ConfigEntry<float> CornerTilt;
        internal static ConfigEntry<float> CornerTiltSpeed;
        internal static ConfigEntry<float> FireRaise;
        internal static ConfigEntry<bool> MuzzleShove;

        internal static ConfigEntry<float> WeaponInertiaHip;
        internal static ConfigEntry<float> WeaponInertiaAds;
        internal static ConfigEntry<float> WeaponInertiaPistolHip;
        internal static ConfigEntry<float> WeaponInertiaPistolAds;

        internal static Quaternion LeadLocal = Quaternion.identity;
        internal static bool HasLead;

        internal static Vector2 WeaponDeadOffset;
        internal static float ThirdWeight;
        internal static Vector3 HeadPosition;

        internal static ConfigEntry<bool> DebugLogging;

        internal const float AdsEnterSpeed = 8f;
        internal const float AdsExitSpeed = 8f;

        internal const string SecGeneral = "1. General";
        internal const string SecPosition = "2. Camera Position";
        internal const string SecRotation = "3. Camera Rotation";
        internal const string SecMotion = "4. Camera Motion";
        internal const string SecWeapon = "5. Weapon";
        internal const string SecOverlay = "6. Overlay";
        internal const string SecLens = "7. Lens";
        internal const string SecSounds = "8. Sounds";
        internal const string SecBody = "9. Body";
        internal const string SecDebug = "99. Debug";

        private void Awake()
        {
            Log = Logger;

            useGUILayout = false;

            ConfigMigration.Begin(Config);

            Enabled = ConfigMigration.Bind(
                SecGeneral, "Enabled", true,
                "Turns the body camera on or off.",
                null, 100, "BodyCam", "Enabled");

            ToggleKey = ConfigMigration.Bind(
                SecGeneral, "Toggle Key", KeyCode.Backslash,
                "Key that turns BodyCam on or off.",
                null, 90, "BodyCam", "Toggle Key");

            ThirdPersonKey = ConfigMigration.Bind(
                SecGeneral, "Third Person Key", KeyCode.Z,
                "Key that switches the third person camera.",
                null, 88, null, null);

            BodyCamGunCam.BindConfig();

            BodyCamMenu.BindConfig(Config);

            BodyCamAdjust.BindConfig();

            OffsetX = ConfigMigration.Bind(
                SecPosition, "Offset X", 0.11f,
                "Camera position left / right from the chest (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 100, "BodyCam", "Offset X");

            OffsetY = ConfigMigration.Bind(
                SecPosition, "Offset Y", 0.10f,
                "Camera position up / down from the chest (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 90, "BodyCam", "Offset Y");

            OffsetZ = ConfigMigration.Bind(
                SecPosition, "Offset Z", 0.08f,
                "Camera position forward / back from the chest (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 80, "BodyCam", "Offset Z");

            PistolOffsetX = ConfigMigration.Bind(
                SecPosition, "Pistol Offset X", 0.02f,
                "Camera left / right with a pistol or revolver (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 78, SecPosition, "Offset X");

            PistolOffsetY = ConfigMigration.Bind(
                SecPosition, "Pistol Offset Y", 0.07f,
                "Camera up / down with a pistol or revolver (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 77, SecPosition, "Offset Y");

            PistolOffsetZ = ConfigMigration.Bind(
                SecPosition, "Pistol Offset Z", 0.06f,
                "Camera forward / back with a pistol or revolver (m).",
                new AcceptableValueRange<float>(-0.50f, 0.50f), 76, SecPosition, "Offset Z");

            HipAimOffsetUp = ConfigMigration.Bind(
                SecPosition, "Hip Aim Offset Up", 0.00f,
                "Tilts the camera up / down relative to the gun, not while aiming.",
                new AcceptableValueRange<float>(-20.00f, 20.00f), 70, "Weapon", "Hip Aim Offset Up");

            HipAimOffsetSide = ConfigMigration.Bind(
                SecPosition, "Hip Aim Offset Side", 0.00f,
                "Turns the camera left / right relative to the gun, not while aiming.",
                new AcceptableValueRange<float>(-20.00f, 20.00f), 69, null, null);

            HipAimOffsetRoll = ConfigMigration.Bind(
                SecPosition, "Hip Aim Offset Roll", 0.00f,
                "Tips the picture round to the left / right (+ = clockwise), not while aiming.",
                new AcceptableValueRange<float>(-20.00f, 20.00f), 68, null, null);

            ThirdPersonX = ConfigMigration.Bind(
                SecPosition, "Third Person X", 0.66f,
                "Third person: camera left / right (m).",
                new AcceptableValueRange<float>(-1.50f, 1.50f), 60, null, null);

            ThirdPersonY = ConfigMigration.Bind(
                SecPosition, "Third Person Y", 0.01f,
                "Third person: camera up / down (m).",
                new AcceptableValueRange<float>(-1.00f, 2.00f), 59, null, null);

            ThirdPersonZ = ConfigMigration.Bind(
                SecPosition, "Third Person Z", -1.37f,
                "Third person: camera forward / back (m, behind = minus).",
                new AcceptableValueRange<float>(-4.00f, 0.50f), 58, null, null);

            ShoulderSwapKey = ConfigMigration.Bind(
                SecBody, "Shoulder Swap Key", KeyCode.LeftControl,
                "Third person: moves the camera to the other shoulder and back.",
                null, 60, null, null);

            ThirdPersonInertia = ConfigMigration.Bind(
                SecBody, "Third Person Inertia", 15.00f,
                "Third person: camera turn lag from the hip (0 = none, off while aiming).",
                new AcceptableValueRange<float>(0.00f, 25.00f), 55, null, null);

            ThirdPersonShake = ConfigMigration.Bind(
                SecBody, "Third Person Shake", 0.30f,
                "Third person: camera shake strength.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 50, null, null);

            ThirdPersonAdsChest = ConfigMigration.Bind(
                SecBody, "Third Person ADS Chest", false,
                "Third person: aiming moves the camera to the chest at once.",
                null, 58, null, null);

            ThirdPersonVisionEffects = ConfigMigration.Bind(
                SecBody, "Third Person Vision Effects", true,
                "Third person: On - the night vision / thermal effect stays (without the masks). Off - no night vision / thermal effect in third person.",
                null, 57, null, null);

            KeepCameraRotation = ConfigMigration.Bind(
                SecRotation, "Keep Camera Rotation", true,
                "On: chest camera that follows the body with inertia. Off: at Tarkov's camera, inertia stays.",
                null, 100, "BodyCam", "Keep Camera Rotation");

            RotationInertia = ConfigMigration.Bind(
                SecRotation, "Rotation Inertia", 15.00f,
                "How quickly the camera follows body and mouse rotation.",
                new AcceptableValueRange<float>(3.0f, 50.0f), 90, "BodyCam", "Rotation Inertia");

            DeadZoneModeCfg = ConfigMigration.Bind(
                SecRotation, "Dead Zone Mode", DeadZoneMode.Camera,
                "Off, Camera (the aim moves, the camera stays) or Weapon (the camera moves, the gun stays).",
                null, 90, null, null);

            CameraDeadZone = ConfigMigration.Bind(
                SecRotation, "Camera Dead Zone", 15.00f,
                "Angle the aim moves before the camera follows (degrees).",
                new AcceptableValueRange<float>(0.00f, 50.00f), 89, null, null);

            WeaponDeadZone = ConfigMigration.Bind(
                SecRotation, "Weapon Dead Zone", 10.00f,
                "Angle the camera turns before the gun follows (degrees, hip only).",
                new AcceptableValueRange<float>(0.00f, 30.00f), 88, null, null);

            WeaponInertiaHip = ConfigMigration.Bind(
                SecRotation, "Weapon Inertia Hip", 1.50f,
                "Weapon swing when you turn, from the hip.",
                new AcceptableValueRange<float>(0.00f, 3.00f), 87, null, null);

            WeaponInertiaAds = ConfigMigration.Bind(
                SecRotation, "Weapon Inertia ADS", 1.50f,
                "Weapon swing when you turn, while aiming.",
                new AcceptableValueRange<float>(0.00f, 3.00f), 85, null, null);

            WeaponInertiaPistolHip = ConfigMigration.Bind(
                SecRotation, "Weapon Inertia Pistol Hip", 3.00f,
                "Pistol swing when you turn, from the hip.",
                new AcceptableValueRange<float>(0.00f, 3.00f), 84, null, null);

            WeaponInertiaPistolAds = ConfigMigration.Bind(
                SecRotation, "Weapon Inertia Pistol ADS", 0.60f,
                "Pistol swing when you turn, while aiming.",
                new AcceptableValueRange<float>(0.00f, 3.00f), 83, null, null);

            CameraLookDownLimit = ConfigMigration.Bind(
                SecRotation, "Camera Look Down Limit", -85.00f,
                "How far down the chest camera can look.",
                new AcceptableValueRange<float>(-85.00f, -40.00f), 75, null, null);

            LeanRollStrength = ConfigMigration.Bind(
                SecRotation, "Lean Roll Strength", 3.00f,
                "Camera tilt when leaning with Q / E.",
                new AcceptableValueRange<float>(0.00f, 3.00f), 80, "BodyCam", "Lean Roll Strength");

            CameraShakeMultiplier = ConfigMigration.Bind(
                SecMotion, "Camera Shake Multiplier", 1.00f,
                "Overall strength of the walk / run camera motion.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 100, "BodyCam", "Camera Shake Multiplier");

            MotionSmoothing = ConfigMigration.Bind(
                SecMotion, "Motion Smoothing", 0.30f,
                "Softens the walk / run motion (nod, sway, rocking, tilt): 0 = as is, higher = softer.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 99, null, null);

            StepNod = ConfigMigration.Bind(
                SecMotion, "Step Nod", 0.80f,
                "The view nods with every step.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 95, null, null);

            AdsStepNod = ConfigMigration.Bind(
                SecMotion, "ADS Step Nod", 1.00f,
                "The view nods with every step while aiming.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 94, null, null);

            RandomSway = ConfigMigration.Bind(
                SecMotion, "Random Sway", 0.70f,
                "Small uneven wandering of the view while you move.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 90, null, null);

            IdleShake = ConfigMigration.Bind(
                SecMotion, "Idle Shake", 1.00f,
                "Small camera shake while you stand still (0 = off).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 89, null, null);

            IdleShakeInMotion = ConfigMigration.Bind(
                SecMotion, "Idle Shake In Motion", true,
                "Idle Shake keeps going while you move.",
                null, 89, null, null);

            StepRollStrength = ConfigMigration.Bind(
                SecMotion, "Step Roll Strength", 0.10f,
                "The camera rocks left / right on every step.",
                new AcceptableValueRange<float>(0.00f, 2.50f), 88, null, null);

            TurnRoll = ConfigMigration.Bind(
                SecMotion, "Turn Roll", 2.00f,
                "The picture tips outward when you turn.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 85, null, null);

            LookShake = ConfigMigration.Bind(
                SecMotion, "Look Shake", 2.00f,
                "The picture shakes a little while you move the mouse (more when fast or running).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 84, null, null);

            BodyTiltStrength = ConfigMigration.Bind(
                SecMotion, "Body Tilt Strength", 0.50f,
                "Camera leans with your movement: forward, sideways, into turns.",
                new AcceptableValueRange<float>(0.00f, 4.00f), 80, "BodyCam", "Body Tilt Strength");

            ShotReactionStrength = ConfigMigration.Bind(
                SecMotion, "Shot Reaction 1", 5.00f,
                "Camera reaction to every shot.",
                new AcceptableValueRange<float>(0.00f, 12.00f), 60, SecMotion, "Shot Reaction Strength");

            ShotReaction2 = ConfigMigration.Bind(
                SecMotion, "Shot Reaction 2", 1.49f,
                "Camera reaction to every shot.",
                new AcceptableValueRange<float>(0.00f, 12.00f), 60, null, null);

            HitImpact = ConfigMigration.Bind(
                SecMotion, "Hit Impact", 1.00f,
                "Camera jolt when you are hit (0 = off).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 59, null, null);

            ExplosionImpact = ConfigMigration.Bind(
                SecMotion, "Explosion Impact", 1.00f,
                "Camera shake from explosions, weaker with distance up to 50 m (0 = off).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 58, null, null);

            FallImpact = ConfigMigration.Bind(
                SecMotion, "Fall Impact", 2.00f,
                "Camera shake when you land from a height, from 0.5 m, full at 3 m (0 = off).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 57, null, null);

            IdleMotionStrength = ConfigMigration.Bind(
                SecWeapon, "Idle Motion", 1.00f,
                "Weapon breathing while standing.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 85, null, null);

            WalkMotionStrength = ConfigMigration.Bind(
                SecWeapon, "Walk Motion", 1.00f,
                "Weapon motion while walking.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 83, null, null);

            AdsIdleMotionStrength = ConfigMigration.Bind(
                SecWeapon, "ADS Idle Motion", 0.30f,
                "Weapon breathing while aiming (off while holding breath).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 81, null, null);

            AdsWalkMotionStrength = ConfigMigration.Bind(
                SecWeapon, "ADS Walk Motion", 0.30f,
                "Weapon motion while walking and aiming.",
                new AcceptableValueRange<float>(0.00f, 2.00f), 79, null, null);

            WalkSlow = ConfigMigration.Bind(SecWeapon, "Walk Slow", 0.40f,
                "Walk Motion at a slow walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 82, null, null);
            WalkNormal = ConfigMigration.Bind(SecWeapon, "Walk Normal", 1.37f,
                "Walk Motion at a normal walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 82, null, null);
            WalkFast = ConfigMigration.Bind(SecWeapon, "Walk Fast", 0.80f,
                "Walk Motion at a fast walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 82, null, null);
            WalkSprint = ConfigMigration.Bind(SecWeapon, "Walk Sprint", 1.00f,
                "Walk Motion while sprinting.", new AcceptableValueRange<float>(0.00f, 3.00f), 82, null, null);
            AdsWalkSlow = ConfigMigration.Bind(SecWeapon, "ADS Walk Slow", 1.00f,
                "ADS Walk Motion at a slow walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 78, null, null);
            AdsWalkNormal = ConfigMigration.Bind(SecWeapon, "ADS Walk Normal", 1.00f,
                "ADS Walk Motion at a normal walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 78, null, null);
            AdsWalkFast = ConfigMigration.Bind(SecWeapon, "ADS Walk Fast", 1.00f,
                "ADS Walk Motion at a fast walk.", new AcceptableValueRange<float>(0.00f, 3.00f), 78, null, null);

            WeaponMotionSpring = ConfigMigration.Bind(SecWeapon, "Weapon Motion Spring", 0.50f,
                "Idle / Walk Motion through a spring: the gun lags and overshoots a little (0 = off).",
                new AcceptableValueRange<float>(0.00f, 1.00f), 76, null, null);

            CornerTilt = ConfigMigration.Bind(
                SecWeapon, "Corner Tilt", 25.00f,
                "Weapon cant at corners and door frames, hip only; the camera stays level (degrees, 0 = off).",
                new AcceptableValueRange<float>(0.00f, 30.00f), 77, null, null);

            CornerTiltSpeed = ConfigMigration.Bind(SecWeapon, "Corner Tilt Speed", 3.00f,
                "How fast the gun tips at corners and straightens back.",
                new AcceptableValueRange<float>(0.50f, 3.00f), 77, null, null);

            FireRaise = ConfigMigration.Bind(
                SecWeapon, "Fire Raise", 2.00f,
                "A pistol rises a little with every shot from the hip (0 = off).",
                new AcceptableValueRange<float>(0.00f, 2.00f), 76, null, null);

            MuzzleShove = ConfigMigration.Bind(
                SecWeapon, "Muzzle Shove", false,
                "From the hip: a bot up close in front gets a shove with the muzzle (1 HP).",
                null, 75, null, null);

            BodyCamOverlay.BindConfig();
            BodyCamBeep.BindConfig();
            BodyCamMagCheck.BindConfig();
            BodyCamHandling.BindConfig();

            BodyCamLens.BindConfig();

            BodyCamSounds.BindConfig();
            BodyCamMic.BindConfig();
            BodyCamBreath.BindConfig();

            BodyCamBodyView.BindConfig();

            DebugLogging = ConfigMigration.Bind(
                SecDebug, "Debug Logging", false,
                "Writes extra status lines to the log.",
                null, 100, "9. Debug", "Debug Logging");

            BodyCamLens.BindDebugConfig();

            ConfigMigration.End();

            _cameraPatches.Add(new BodyCamLateUpdatePatch());
            _cameraPatches.Add(new BodyCamShotPatch());
            _cameraPatches.Add(new BodyCamStepPatch());
            _cameraPatches.Add(new BodyCamAmmoPanelPatch());
            _cameraPatches.Add(new BodyCamCheckAmmoPatch());
            _cameraPatches.Add(new BodyCamHitShotPatch());
            _cameraPatches.Add(new BodyCamFallPatch());
            _cameraPatches.Add(new BodyCamBreathSpeakerPatch());
            _cameraPatches.Add(new BodyCamFlyByPatch());
            _cameraPatches.Add(new BodyCamNightVisionSwitchPatch());
            _cameraPatches.Add(new BodyCamThermalSwitchPatch());
            _weaponPatches.Add(new BodyCamWeaponKickPatch());
            _weaponPatches.Add(new BodyCamOverlapPatch());

            Enabled.SettingChanged += (sender, args) => SyncPatches();
            Enabled.SettingChanged += (sender, args) =>
            {
                if (Enabled.Value)
                    BodyCamOverlay.TriggerConnected();

                BodyCamBeep.Play(Enabled.Value);

                BodyCamAdjust.OnEnabledChanged(Enabled.Value);
            };
            IdleMotionStrength.SettingChanged += (sender, args) => SyncPatches();
            WalkMotionStrength.SettingChanged += (sender, args) => SyncPatches();
            AdsIdleMotionStrength.SettingChanged += (sender, args) => SyncPatches();
            AdsWalkMotionStrength.SettingChanged += (sender, args) => SyncPatches();
            CornerTilt.SettingChanged += (sender, args) => SyncPatches();
            FireRaise.SettingChanged += (sender, args) => SyncPatches();
            MuzzleShove.SettingChanged += (sender, args) => SyncPatches();
            WeaponInertiaAds.SettingChanged += (sender, args) => SyncPatches();
            WeaponInertiaHip.SettingChanged += (sender, args) => SyncPatches();
            WeaponInertiaPistolHip.SettingChanged += (sender, args) => SyncPatches();
            WeaponInertiaPistolAds.SettingChanged += (sender, args) => SyncPatches();
            KeepCameraRotation.SettingChanged += (sender, args) => SyncPatches();
            SyncPatches();

            try
            {
                new BodyCamGameStartedPatch().Enable();
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    "[BodyCamera] Raid start hook unavailable, using the " +
                    "fallback timer: " + ex.Message);
            }

            BodyCamSounds.Create();
            BodyCamMic.Create();
            BodyCamBreath.Create();

            try
            {
                new BodyCamMenuInputPatch().Enable();
                new BodyCamMenuFixedInputPatch().Enable();
            }
            catch (Exception ex)
            {
                Log.LogWarning("[BodyCamera] Menu input block unavailable: " + ex.Message);
            }

            try
            {
                new BodyCamHitNotePatch().Enable();
            }
            catch (Exception ex)
            {
                Log.LogWarning("[BodyCamera] Hit hook unavailable (random readjust only waits after own shots): " + ex.Message);
            }

            try
            {
                BodyCamHit.Subscribe();
            }
            catch (Exception ex)
            {
                Log.LogWarning("[BodyCamera] Explosion event unavailable: " + ex.Message);
            }

            StartCoroutine(BodyCamLens.Loop());

            StartCoroutine(BodyCamGunCam.Loop());

            Log.LogInfo(
                "[BodyCamera] Loaded 1.0.0. Toggle key: " + ToggleKey.Value + ".");
        }

        private static readonly List<ModulePatch> _cameraPatches =
            new List<ModulePatch>();

        private static readonly List<ModulePatch> _weaponPatches =
            new List<ModulePatch>();

        private static void SyncPatches()
        {
            bool camera = Enabled.Value;
            bool weapon = camera && WeaponReady &&
                (IdleMotionStrength.Value > 0.0001f ||
                 WalkMotionStrength.Value > 0.0001f ||
                 AdsIdleMotionStrength.Value > 0.0001f ||
                 AdsWalkMotionStrength.Value > 0.0001f ||
                 CornerTilt.Value > 0.0001f ||
                 FireRaise.Value > 0.0001f ||
                 MuzzleShove.Value ||
                 WeaponInertiaAds.Value > 0.0001f ||
                 WeaponInertiaHip.Value > 0.0001f ||
                 WeaponInertiaPistolHip.Value > 0.0001f ||
                 WeaponInertiaPistolAds.Value > 0.0001f ||
                 KeepCameraRotation.Value);

            try
            {
                for (int i = 0; i < _cameraPatches.Count; i++)
                    SetPatchActive(_cameraPatches[i], camera);

                for (int i = 0; i < _weaponPatches.Count; i++)
                    SetPatchActive(_weaponPatches[i], weapon);
            }
            catch (Exception ex)
            {
                Log.LogError("[BodyCamera] Patch switch error: " + ex);
            }

            if (!camera)
                BodyCamLateUpdatePatch.DeactivateAll();

            if (!weapon)
            {
                BodyCamRecoil.Clear();
                BodyCamRecoil.ClearDelta();
            }
        }

        private static void SetPatchActive(ModulePatch patch, bool active)
        {
            if (active && !patch.IsActive)
                patch.Enable();
            else if (!active && patch.IsActive)
                patch.Disable();
        }

        private void OnGUI()
        {
            try
            {
                if (Event.current != null &&
                    Event.current.type == EventType.Repaint &&
                    BodyCamLens.IsActive())
                {
                    RenderTexture prev = RenderTexture.active;

                    try
                    {
                        BodyCamLens.BeginOverlay();
                        BodyCamOverlay.Draw();
                        BodyCamAimDot.Draw();
                    }
                    finally
                    {
                        RenderTexture.active = prev;
                    }
                }
                else
                {
                    BodyCamOverlay.Draw();
                    BodyCamAimDot.Draw();
                }
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 0)
                    Log.LogError("[BodyCamera] Overlay error: " + ex);
            }

            try
            {
                BodyCamMenu.OnGUI();
            }
            catch (Exception ex)
            {
                if (Time.frameCount % 600 == 0)
                    Log.LogError("[BodyCamera] Menu error: " + ex);
            }
        }

        private void Update()
        {
            BodyCamMenu.Tick();

            if (!BodyCamMenu.EatsKeys && Input.GetKeyDown(ToggleKey.Value))
            {
                if (BodyCamAdjust.HandleToggleKey())
                {
                    Log.LogInfo(
                        BodyCamAdjust.PendingOff
                            ? "[BodyCamera] Turning off (camera adjust)..."
                            : "[BodyCamera] Turn-off cancelled.");
                }
                else
                {
                    Enabled.Value = !Enabled.Value;

                    Log.LogInfo(
                        "[BodyCamera] " +
                        (Enabled.Value ? "ENABLED" : "DISABLED"));
                }
            }

            if (!BodyCamMenu.EatsKeys && Enabled.Value && ThirdPersonKey.Value != KeyCode.None &&
                Input.GetKeyDown(ThirdPersonKey.Value))
            {
                ThirdPerson = !ThirdPerson;
                Log.LogInfo("[BodyCamera] Third person " + (ThirdPerson ? "ON" : "OFF"));
            }

            if (!BodyCamMenu.EatsKeys && Enabled.Value && BodyCamGunCam.CfgKey.Value != KeyCode.None &&
                Input.GetKeyDown(BodyCamGunCam.CfgKey.Value))
            {
                BodyCamGunCam.Toggle();
            }

            if (!BodyCamMenu.EatsKeys && Enabled.Value && ThirdPerson &&
                ShoulderSwapKey.Value != KeyCode.None && Input.GetKeyDown(ShoulderSwapKey.Value))
            {
                ShoulderSwapped = !ShoulderSwapped;
                Log.LogInfo("[BodyCamera] Shoulder " + (ShoulderSwapped ? "swapped" : "normal"));
            }

            UpdateWeaponReady();
            BodyCamBeep.Tick();
            BodyCamAdjust.Tick();
        }

        internal static bool WeaponReady;

        private const float WeaponStartDelay = 1.5f;
        private const float WeaponStartFallback = 10f;

        internal static GameWorld StartedWorld;
        internal static float GameStartTime = -1f;

        private static GameWorld _raidWorld;
        private static float _weaponSeenTime = -1f;

        private static void UpdateWeaponReady()
        {
            bool ready = false;

            try
            {
                GameWorld world = Singleton<GameWorld>.Instance;
                var player = world != null ? world.MainPlayer : null;

                if (world != _raidWorld)
                {
                    _raidWorld = world;
                    _weaponSeenTime = -1f;
                    BodyCamRecoil.ResetForRaid();
                }

                if (player != null)
                {
                    if (WeaponReady)
                    {
                        ready = true;
                    }
                    else
                    {
                        var pwa = player.ProceduralWeaponAnimation;

                        bool hasWeapon =
                            player.HandsController is EFT.Player.FirearmController &&
                            pwa != null &&
                            pwa.HandsContainer != null &&
                            pwa.HandsContainer.WeaponRootAnim != null;

                        if (hasWeapon)
                        {
                            float now = Time.unscaledTime;

                            if (_weaponSeenTime < 0f)
                                _weaponSeenTime = now;

                            bool started =
                                StartedWorld == world && GameStartTime >= 0f;

                            if (started)
                            {
                                float from = Mathf.Max(GameStartTime, _weaponSeenTime);
                                ready = now >= from + WeaponStartDelay;
                            }
                            else
                            {
                                ready = now >= _weaponSeenTime + WeaponStartFallback;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ready = false;

                if (DebugLogging.Value)
                    Log.LogWarning("[BodyCamera] Weapon ready check error: " + ex.Message);
            }

            if (ready == WeaponReady)
                return;

            WeaponReady = ready;
            SyncPatches();

            Log.LogInfo(
                ready
                    ? "[BodyCamera] Raid started - weapon sway/kick attached."
                    : "[BodyCamera] Weapon sway/kick detached until the raid starts.");
        }
    }

    internal sealed class BodyCamGameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod(
                "OnGameStarted",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix(GameWorld __instance)
        {
            Plugin.StartedWorld = __instance;
            Plugin.GameStartTime = Time.unscaledTime;
            BodyCamOverlay.TriggerConnected();

            BodyCamBeep.ResetAudio();
            BodyCamSounds.ResetAudio();
            BodyCamBodyView.ResetForRaid();
            BodyCamHit.Reset();
            BodyCamBreath.ResetAudio();

            if (Plugin.Enabled.Value)
            {
                float beep = BodyCamAdjust.ScheduleRaidStart();
                BodyCamBeep.Schedule(beep >= 0f ? beep : 0.35f);
            }
        }
    }

    internal sealed class BodyCamState
    {
        internal bool Active;
        internal PlayerCameraController Controller;
        internal float NextResolveTime;
        internal Transform Anchor;
        internal Transform CameraTransform;
        internal Transform LocalPlayerTransform;
        internal string AnchorName;
        internal float LogTimer;

        internal Vector3 CurrentPosition;
        internal bool HasCurrentPosition;

        internal Vector3 ChestLocal;
        internal bool HasChestLocal;
        internal Vector3 LastAimForward;

        internal Quaternion InertialRotation;
        internal float PistolBlend;
        internal bool HasInertialRotation;

        internal float LookYawRelativeToBody;
        internal float LookPitch;
        internal float LookRoll;
        internal float RollHoldTimer;

        internal Vector3 ShotOffset;
        internal Vector3 ShotVelocity;
        internal Vector3 ShotPush;
        internal float ShotSign;
        internal float LastShotTime = -10f;
        internal int ShotFrame = -1;

        internal readonly System.Collections.Generic.List<Vector4> Shot2 =
            new System.Collections.Generic.List<Vector4>();
        internal Vector3 Shot2Smooth;
        internal Vector3 Shot2Velocity;
        internal int Shot2Frame = -1;

        internal int PendingSteps;
        internal int StepCount;
        internal float StepTime = 10f;
        internal float StepInterval = 0.667f;
        internal float StepSign = 1f;
        internal float StepAmp;
        internal float StepRollAmp = 1f;
        internal float StepDistance;
        internal float LastRealStepTime = -100f;
        internal bool StepFallback;
        internal float StepPitchUp;
        internal float StepYawRight;
        internal float StepRollRight;

        internal float StepPitchVel;
        internal float StepYawVel;
        internal float StepRollVel;

        internal bool HasLookOffset;

        internal bool WasAiming;
        internal bool HasAimingState;
        internal float AdsBlend;
        internal float ThirdBlend;
        internal bool ThirdAdsCut;
        internal float DeadYaw;
        internal float DeadPitch;
        internal float DeadLastYaw;
        internal float DeadLastPitch;
        internal float DeadStill;
        internal float GunDeadYaw;
        internal float GunDeadPitch;
        internal Quaternion LeadInertial = Quaternion.identity;
        internal float ThirdDist = -1f;
        internal float ShoulderBlend;

        internal Vector3 LastTarkovPosition;
        internal Quaternion LastTarkovRotation;
        internal bool HasLastTarkovState;

        internal Vector3 StableAdsPosition;
        internal Quaternion StableAdsRotation;
        internal bool HasStableAdsState;

        internal Vector3 AdsCandidatePosition;
        internal Quaternion AdsCandidateRotation;
        internal float AdsCandidateTimer;
        internal bool HasAdsCandidate;

        internal Vector3 AdsExitPosition;
        internal Vector3 AdsExitNormalPosition;
        internal bool HasAdsExitPosition;

        internal Vector3 LastPlayerPosition;
        internal bool HasLastPlayerPosition;
        internal float SmoothedMoveSpeed;
        internal float SmoothedVerticalSpeed;
        internal Vector3 SmoothedVelocity;
        internal float StridePhase;
        internal int StepIndex;
        internal float StepVariation = 1f;
        internal float StepVariationTarget = 1f;
        internal float LastBodyYaw;
        internal float SmoothedYawRate;
        internal float TiltPitch;
        internal float TiltRoll;
        internal float IdleTime;
        internal float IdleWeight;
        internal Vector3 IdleShake;

        internal float SwayWeight;
        internal float SwayTime;

        internal float VanillaBlend;
        internal bool HasVanillaBlend;

        internal bool HasChestRest;
        internal Quaternion ChestRest = Quaternion.identity;
        internal float ChestPitch;

        internal bool HasSmoothMotion;
        internal Vector3 SmoothRot1;
        internal Vector3 SmoothRot2;
        internal Vector3 SmoothPos1;
        internal Vector3 SmoothPos2;

        internal bool HasTurnYaw;
        internal float TurnLastYaw;
        internal float TurnYawRate;
        internal float TurnRoll;
        internal float TurnRollVel;
    }

    internal sealed class BodyCamLateUpdatePatch : ModulePatch
    {
        private static int _gunStepCount;
        private static float _gunStepInterval = 0.55f;
        private static float _gunStepStamp = -100f;

        internal static bool GetGunStepInfo(out int stepCount, out float interval)
        {
            stepCount = _gunStepCount;
            interval = _gunStepInterval;
            return Time.time - _gunStepStamp < 0.25f;
        }

        private static readonly Dictionary<int, BodyCamState> States =
            new Dictionary<int, BodyCamState>();

        private static readonly float[] StepPitchUpCurve =
        {
            0.000f, -0.163f, -0.167f, 0.061f, 0.282f, 0.356f, 0.428f,
            0.460f, 0.465f, 0.464f, 0.438f, 0.402f, 0.339f, 0.281f,
            0.216f, 0.168f, 0.129f, 0.123f, 0.115f, 0.119f, 0.000f
        };

        private static readonly float[] StepRollRightCurve =
        {
            0.000f, -0.200f, -0.560f, -0.924f, -1.205f, -1.451f, -1.560f,
            -1.406f, -1.093f, -0.737f, -0.417f, -0.074f, 0.129f, 0.244f,
            0.279f, 0.253f, 0.207f, 0.152f, 0.068f, -0.005f, 0.000f
        };

        private static readonly float[] StepYawRightCurve =
        {
            0.000f, 0.040f, 0.074f, 0.084f, 0.100f, 0.096f, 0.082f,
            0.073f, 0.084f, 0.102f, 0.119f, 0.137f, 0.137f, 0.137f,
            0.139f, 0.128f, 0.099f, 0.058f, 0.027f, 0.004f, 0.000f
        };

        private const float StepCurveSpan = 0.632f;

        internal const float StepTemplateDuration = 0.667f;

        private const float ShotFreq = 5f;
        private const float ShotDamping = 0.45f;
        private const float ShotBurstGap = 0.35f;
        private const float ShotPushTau = 0.03f;
        private const float ShotPeakPerStrength = 2f / 6f;
        private static float _shotPushForPeak = -1f;

        private static float ShotPushForPeak()
        {
            if (_shotPushForPeak > 0f)
                return _shotPushForPeak;

            float w = 2f * Mathf.PI * ShotFreq;
            float x = 0f, v = 0f, push = 1f, peak = 0f;
            const float h = 1f / 480f;

            for (int i = 0; i < 240; i++)
            {
                v += (push - w * w * x - 2f * ShotDamping * w * v) * h;
                x += v * h;
                push *= Mathf.Exp(-h / ShotPushTau);
                peak = Mathf.Max(peak, Mathf.Abs(x));
            }

            _shotPushForPeak = peak > 0f ? 1f / peak : 1f;
            return _shotPushForPeak;
        }

        internal static float SamplePhase(float[] curve, float u)
        {
            float x = Mathf.Clamp01(u) * (curve.Length - 1);
            int i = Mathf.Min((int)x, curve.Length - 2);
            return Mathf.Lerp(curve[i], curve[i + 1], x - i);
        }

        internal static float SampleTime(float[] curve, float t)
        {
            float x = t * 60f;

            if (x < 0f || x >= curve.Length - 1)
                return 0f;

            int i = (int)x;
            return Mathf.Lerp(curve[i], curve[i + 1], x - i);
        }

        internal static void ApplyShotReaction()
        {
            float strength = Mathf.Max(0f, Plugin.ShotReactionStrength.Value);
            float strength2 = Mathf.Max(0f, Plugin.ShotReaction2.Value);

            foreach (var pair in States)
            {
                var state = pair.Value;

                if (state == null || !state.Active || state.Controller == null)
                    continue;

                state.RollHoldTimer = 0.35f;

                if (strength2 > 0.0001f && state.Shot2.Count < 32)
                {
                    state.Shot2.Add(new Vector4(
                        Time.unscaledTime,
                        UnityEngine.Random.Range(0.7f, 0.8f),
                        UnityEngine.Random.Range(-0.3f, -0.2f),
                        UnityEngine.Random.Range(1.8f, 2.7f)));
                }

                if (strength < 0.0001f)
                    continue;

                float now = Time.unscaledTime;

                if (state.ShotSign == 0f || now - state.LastShotTime > ShotBurstGap)
                    state.ShotSign = UnityEngine.Random.value < 0.5f ? 1f : -1f;

                state.LastShotTime = now;
                float sign = state.ShotSign;
                Vector3 dir = new Vector3(
                    1f,
                    UnityEngine.Random.Range(-0.35f, 0.35f),
                    UnityEngine.Random.Range(0.6f, 1.0f));

                state.ShotPush += dir * (sign * UnityEngine.Random.Range(0.8f, 1.2f) *
                                         strength * ShotPeakPerStrength * ShotPushForPeak());
            }
        }

        private static Vector3 EvaluateShotReaction(BodyCamState state)
        {
            if (state.ShotFrame != Time.frameCount)
            {
                state.ShotFrame = Time.frameCount;

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 480f));
                float h = dt / steps;
                float w = 2f * Mathf.PI * ShotFreq;
                float decay = Mathf.Exp(-h / ShotPushTau);

                for (int n = 0; n < steps; n++)
                {
                    state.ShotVelocity += (state.ShotPush - w * w * state.ShotOffset -
                                           2f * ShotDamping * w * state.ShotVelocity) * h;
                    state.ShotOffset += state.ShotVelocity * h;
                    state.ShotPush *= decay;
                }

                if (state.ShotOffset.sqrMagnitude < 1e-8f && state.ShotVelocity.sqrMagnitude < 1e-6f &&
                    state.ShotPush.sqrMagnitude < 1e-6f)
                {
                    state.ShotOffset = Vector3.zero;
                    state.ShotVelocity = Vector3.zero;
                    state.ShotPush = Vector3.zero;
                }
            }

            float up = state.ShotOffset.x;
            float right = state.ShotOffset.y;
            float rollRight = state.ShotOffset.z;

            float limit = Mathf.Max(1f, Plugin.ShotReactionStrength.Value);
            up = SoftLimit(up, 0.8f * limit);
            right = SoftLimit(right, 0.4f * limit);
            rollRight = SoftLimit(rollRight, 0.8f * limit);

            Vector3 two = EvaluateShot2(state);

            up += two.x;
            right += two.y;
            rollRight += two.z;

            return new Vector3(-up, right, -rollRight);
        }

        private static readonly float[] Shot2PitchKeys = { 0f, 0f, 0.089f, 1f, 0.319f, -0.49f, 0.514f, 0.29f, 0.748f, -0.18f, 1f, 0f };
        private static readonly float[] Shot2YawKeys = { 0f, 0f, 0.18f, 1f, 0.715f, -0.21f, 1f, 0f };
        private static readonly float[] Shot2RollKeys = { 0f, 0f, 0.16f, 1f, 0.412f, -0.18f, 1f, 0f };
        private const float Shot2Rate = 2f;

        private static float SampleKeys(float[] keys, float u)
        {
            if (u <= 0f || u >= 1f)
                return 0f;

            for (int i = 0; i + 3 < keys.Length; i += 2)
            {
                float a = keys[i], b = keys[i + 2];

                if (u >= a && u <= b)
                {
                    float s = b > a ? (u - a) / (b - a) : 1f;
                    s = s * s * (3f - 2f * s);
                    return Mathf.Lerp(keys[i + 1], keys[i + 3], s);
                }
            }

            return 0f;
        }

        private static Vector3 EvaluateShot2(BodyCamState state)
        {
            if (state.Shot2Frame != Time.frameCount)
            {
                state.Shot2Frame = Time.frameCount;

                float now = Time.unscaledTime;
                float k = Mathf.Max(0f, Plugin.ShotReaction2.Value);
                Vector3 target = Vector3.zero;

                for (int i = state.Shot2.Count - 1; i >= 0; i--)
                {
                    Vector4 s = state.Shot2[i];
                    float u = (now - s.x) * Shot2Rate;

                    if (u >= 1f)
                    {
                        state.Shot2.RemoveAt(i);
                        continue;
                    }

                    target.x += s.y * SampleKeys(Shot2PitchKeys, u);
                    target.y += s.z * SampleKeys(Shot2YawKeys, u);
                    target.z += s.w * SampleKeys(Shot2RollKeys, u);
                }

                target *= k;

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 480f));
                float h = dt / steps;
                float w = 2f * Mathf.PI * ShotFreq;

                for (int n = 0; n < steps; n++)
                {
                    state.Shot2Velocity += (w * w * (target - state.Shot2Smooth) -
                                            2f * ShotDamping * w * state.Shot2Velocity) * h;
                    state.Shot2Smooth += state.Shot2Velocity * h;
                }

                if (state.Shot2.Count == 0 && state.Shot2Smooth.sqrMagnitude < 1e-8f &&
                    state.Shot2Velocity.sqrMagnitude < 1e-6f)
                {
                    state.Shot2Smooth = Vector3.zero;
                    state.Shot2Velocity = Vector3.zero;
                }
            }

            return state.Shot2Smooth;
        }

        private static float SoftLimit(float x, float max)
        {
            if (max <= 0f)
                return 0f;

            float t = x / max;
            float e = Mathf.Exp(-2f * Mathf.Abs(t));
            return Mathf.Sign(t) * max * (1f - e) / (1f + e);
        }

        private static bool _loggedFootstep;

        internal static void OnFootstep()
        {
            if (!_loggedFootstep)
            {
                _loggedFootstep = true;
                Plugin.Log.LogInfo(
                    "[BodyCamera] Footstep events received: step motion is synced to Tarkov footsteps.");
            }

            foreach (var pair in States)
            {
                var state = pair.Value;

                if (state != null && state.Active)
                    state.PendingSteps++;
            }

            BodyCamLens.OnFootstep();
        }

        internal static void StepSpring(
            ref Vector3 angle,
            ref Vector3 velocity,
            float frequency,
            float damping,
            float dt)
        {
            float omega = 2f * Mathf.PI * frequency;
            float k = omega * omega;
            float c = 2f * damping * omega;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 25);
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                velocity += (-k * angle - c * velocity) * h;
                angle += velocity * h;
            }

            if (angle.sqrMagnitude < 1e-8f && velocity.sqrMagnitude < 1e-6f)
            {
                angle = Vector3.zero;
                velocity = Vector3.zero;
            }
        }

        internal static void DeactivateAll()
        {
            Plugin.HasLead = false;
            Plugin.WeaponDeadOffset = Vector2.zero;
            Plugin.ThirdWeight = 0f;
            BodyCamGunCam.Deactivate();
            BodyCamBodyView.Clear();
            BodyCamThirdEffects.Tick(null, false);

            foreach (var pair in States)
            {
                if (pair.Value != null)
                {
                    pair.Value.Active = false;
                    ClearReactions(pair.Value);
                }
            }
        }

        private static void ClearReactions(BodyCamState state)
        {
            state.ShotOffset = Vector3.zero;
            state.ShotVelocity = Vector3.zero;
            state.ShotPush = Vector3.zero;
            state.Shot2.Clear();
            state.Shot2Smooth = Vector3.zero;
            state.Shot2Velocity = Vector3.zero;
        }

        private static readonly Type PlayerBodyType =
            Type.GetType("EFT.PlayerBody, Assembly-CSharp");

        private static bool _warnedNoBodyType;
        private static bool _warnedBodyMissing;
        private static bool _warnedNoAnchor;
        private static bool _warnedNoCamera;
        private static bool _dumpedHierarchy;

        protected override MethodBase GetTargetMethod()
        {
            var type = typeof(PlayerCameraController);

            return type.GetMethod(
                "LateUpdate",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPrefix]
        private static void Prefix(PlayerCameraController __instance)
        {
            try
            {
                var state = GetState(__instance);

                if (!state.Active || !Plugin.Enabled.Value)
                    return;

                if (!state.HasLastTarkovState)
                    return;

                if (state.CameraTransform == null)
                    return;

                state.CameraTransform.position = state.LastTarkovPosition;
                state.CameraTransform.rotation = state.LastTarkovRotation;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[BodyCamera] Prefix camera restore error: " + ex);
            }
        }

        [PatchPostfix]
        private static void Postfix(PlayerCameraController __instance)
        {
            try
            {
                var current = GetState(__instance);

                bool enabled = Plugin.Enabled.Value;

                if (enabled != current.Active)
                {
                    current.Active = enabled;

                    if (enabled)
                    {
                        ResetRuntimeState(current);
                        Resolve(__instance, current, true);
                    }
                }

                if (!current.Active)
                    return;

                Camera activeCamera = FindCamera(__instance);

                if (activeCamera != null &&
                    current.CameraTransform != activeCamera.transform)
                {
                    current.CameraTransform = activeCamera.transform;
                    current.HasLastTarkovState = false;

                    Plugin.Log.LogInfo(
                        "[BodyCamera] Active camera changed -> " +
                        activeCamera.name + ". Rebinding BodyCam.");
                }

                if (current.CameraTransform == null ||
                    current.Anchor == null)
                {
                    if (Time.unscaledTime < current.NextResolveTime)
                        return;

                    current.NextResolveTime = Time.unscaledTime + 0.5f;

                    if (!Resolve(__instance, current, false))
                        return;
                }

                var player = Singleton<GameWorld>.Instance?.MainPlayer;

                if (player == null)
                    return;

                var playerTransform = player.GetComponent<Transform>();

                if (playerTransform == null)
                    return;

                if (current.LocalPlayerTransform != playerTransform)
                {
                    current.LocalPlayerTransform = playerTransform;
                    current.Anchor = null;
                    ResetRuntimeState(current);

                    if (!Resolve(__instance, current, true))
                        return;
                }

                if (current.Anchor == null ||
                    current.CameraTransform == null)
                    return;

                Vector3 bodyRight = playerTransform.right;

                Vector3 bodyForward =
                    Vector3.ProjectOnPlane(
                        playerTransform.forward,
                        Vector3.up);

                if (bodyForward.sqrMagnitude < 0.001f)
                    bodyForward = Vector3.forward;
                else
                    bodyForward.Normalize();

                Vector3 eyePosition = current.CameraTransform.position;

                Vector3 aimForward = FlatFacing(current.CameraTransform.rotation);

                if (aimForward.sqrMagnitude < 0.0001f)
                    aimForward = bodyForward;
                else
                    aimForward.Normalize();

                Quaternion aimYaw = Quaternion.LookRotation(aimForward, Vector3.up);

                Vector3 chestRel =
                    Quaternion.Inverse(aimYaw) * (current.Anchor.position - eyePosition);

                float chestDt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.1f);

                if (!current.HasChestLocal || chestRel.sqrMagnitude > 1f)
                {
                    current.ChestLocal = chestRel;
                    current.HasChestLocal = true;
                    current.LastAimForward = aimForward;
                }
                else
                {
                    float yawSpeed =
                        Vector3.Angle(current.LastAimForward, aimForward) / chestDt;

                    float rate = 5f / (1f + yawSpeed / 40f);
                    float k = 1f - Mathf.Exp(-rate * chestDt);

                    current.ChestLocal.x = Mathf.Lerp(current.ChestLocal.x, chestRel.x, k);
                    current.ChestLocal.z = Mathf.Lerp(current.ChestLocal.z, chestRel.z, k);
                    current.ChestLocal.y = chestRel.y;
                    current.LastAimForward = aimForward;
                }

                float pistolTarget = BodyCamRecoil.IsPistolInHands(player) ? 1f : 0f;
                current.PistolBlend = Mathf.MoveTowards(
                    current.PistolBlend, pistolTarget, chestDt / 0.3f);

                Vector3 cameraOffset = Vector3.Lerp(
                    new Vector3(Plugin.OffsetX.Value, Plugin.OffsetY.Value, Plugin.OffsetZ.Value),
                    new Vector3(Plugin.PistolOffsetX.Value, Plugin.PistolOffsetY.Value, Plugin.PistolOffsetZ.Value),
                    Mathf.SmoothStep(0f, 1f, current.PistolBlend));

                Quaternion chestFrame = ChestFrame(current, aimYaw, chestDt);

                Vector3 normalPosition =
                    eyePosition
                    + aimYaw * current.ChestLocal
                    + chestFrame * cameraOffset;

                Vector3 tarkovCameraPosition =
                    current.CameraTransform.position;

                Quaternion tarkovCameraRotation =
                    current.CameraTransform.rotation;

                current.LastTarkovPosition = tarkovCameraPosition;
                Plugin.HeadPosition = tarkovCameraPosition;
                current.LastTarkovRotation = tarkovCameraRotation;
                current.HasLastTarkovState = true;

                Vector3 adsCameraPosition =
                    BodyCamRecoil.RemoveRecoilShift(tarkovCameraPosition);

                var weaponAnimation = player.ProceduralWeaponAnimation;

                bool actualAds =
                    weaponAnimation != null &&
                    weaponAnimation.IsAiming &&
                    !SprintState.IsSprinting(player);

                if (!current.HasAimingState)
                {
                    current.WasAiming = actualAds;
                    current.HasAimingState = true;

                    current.AdsBlend = 0f;
                    current.HasAdsExitPosition = false;
                }
                else if (actualAds != current.WasAiming)
                {
                    if (actualAds)
                    {
                        current.AdsBlend = 0f;
                        current.HasAdsExitPosition = false;

                        if (Plugin.DebugLogging.Value)
                            Plugin.Log.LogInfo(
                                "[BodyCamera] Tarkov ADS entered -> starting BodyCam ADS transition.");
                    }
                    else
                    {
                        current.AdsExitPosition = current.HasCurrentPosition
                            ? current.CurrentPosition
                            : normalPosition;

                        current.AdsExitNormalPosition = normalPosition;
                        current.HasAdsExitPosition = true;
                        current.AdsBlend = 1f;

                        if (Plugin.DebugLogging.Value)
                            Plugin.Log.LogInfo(
                                "[BodyCamera] Tarkov ADS exited -> starting BodyCam ADS exit transition.");
                    }

                    current.WasAiming = actualAds;
                }

                if (actualAds)
                {
                    float adsBlendStep =
                        1f - Mathf.Exp(
                            -Plugin.AdsEnterSpeed *
                            Time.unscaledDeltaTime);

                    current.AdsBlend = Mathf.Lerp(
                        current.AdsBlend,
                        1f,
                        adsBlendStep);
                }
                else if (current.HasAdsExitPosition)
                {
                    float exitBlendStep =
                        1f - Mathf.Exp(
                            -Plugin.AdsExitSpeed *
                            Time.unscaledDeltaTime);

                    current.AdsBlend = Mathf.Lerp(
                        current.AdsBlend,
                        0f,
                        exitBlendStep);

                    if (current.AdsBlend < 0.001f)
                    {
                        current.AdsBlend = 0f;
                        current.HasAdsExitPosition = false;
                    }
                }
                else
                {
                    current.AdsBlend = 0f;
                }

                Vector3 targetPosition;

                if (actualAds)
                {
                    if (current.AdsBlend > 0.995f)
                    {
                        targetPosition = adsCameraPosition;
                    }
                    else
                    {
                        targetPosition =
                            Vector3.Lerp(
                                normalPosition,
                                adsCameraPosition,
                                current.AdsBlend);
                    }
                }
                else if (current.HasAdsExitPosition)
                {
                    Vector3 exitOffset =
                        current.AdsExitPosition - current.AdsExitNormalPosition;

                    Vector3 movingExitPosition =
                        normalPosition + exitOffset;

                    targetPosition =
                        Vector3.Lerp(
                            normalPosition,
                            movingExitPosition,
                            current.AdsBlend);
                }
                else
                {
                    targetPosition = normalPosition;
                }

                float vanillaTarget = Plugin.KeepCameraRotation.Value ? 0f : 1f;

                if (!current.HasVanillaBlend)
                {
                    current.VanillaBlend = vanillaTarget;
                    current.HasVanillaBlend = true;
                }
                else
                {
                    current.VanillaBlend = Mathf.MoveTowards(
                        current.VanillaBlend,
                        vanillaTarget,
                        Time.unscaledDeltaTime / 0.3f);
                }

                float vanilla = Mathf.SmoothStep(0f, 1f, current.VanillaBlend);

                if (vanilla > 0.0001f)
                    targetPosition = Vector3.Lerp(targetPosition, adsCameraPosition, vanilla);

                bool gunCam = BodyCamGunCam.Update(
                    player, current.CameraTransform, tarkovCameraRotation, actualAds);

                BodyCamGunCam.NoteActive(gunCam);

                bool thirdWanted = Plugin.ThirdPerson && !gunCam;
                bool adsCut = thirdWanted && Plugin.ThirdPersonAdsChest.Value && actualAds;

                if (gunCam)
                    current.ThirdBlend = 0f;
                else if (adsCut)
                    current.ThirdBlend = 0f;
                else if (current.ThirdAdsCut && thirdWanted)
                    current.ThirdBlend = 1f;
                else
                    current.ThirdBlend = Mathf.MoveTowards(
                        current.ThirdBlend,
                        thirdWanted ? 1f : 0f,
                        Time.unscaledDeltaTime / 0.35f);

                current.ThirdAdsCut = adsCut;

                float third = Mathf.SmoothStep(0f, 1f, current.ThirdBlend);
                Plugin.ThirdWeight = third;

                if (third > 0.0001f)
                {
                    Vector3 thirdPosition = ThirdPersonPosition(
                        current,
                        playerTransform,
                        eyePosition + aimYaw * current.ChestLocal,
                        aimForward,
                        chestDt);

                    targetPosition = Vector3.Lerp(targetPosition, thirdPosition, third);
                }
                else
                {
                    current.ThirdDist = -1f;
                }

                BodyCamBodyView.Tick(player, current.ThirdBlend > 0.001f || gunCam);
                BodyCamThirdEffects.Tick(Camera.main, current.ThirdBlend > 0.5f || gunCam);

                current.CurrentPosition = targetPosition;
                current.HasCurrentPosition = true;

                Quaternion gameRotation =
                    tarkovCameraRotation;

                float bodyYaw =
                    GetHorizontalYaw(bodyForward);

                float gameYaw =
                    GetHorizontalYaw(FlatFacing(gameRotation));

                float relativeYaw =
                    Mathf.DeltaAngle(bodyYaw, gameYaw);

                Vector3 flatLook =
                    Vector3.ProjectOnPlane(
                        gameRotation * Vector3.forward,
                        Vector3.up);

                float pitch =
                    Mathf.Asin(
                        Mathf.Clamp(
                            Vector3.Dot(
                                gameRotation * Vector3.forward,
                                Vector3.up),
                            -1f,
                            1f))
                    * Mathf.Rad2Deg;

                if (current.RollHoldTimer > 0f)
                {
                    current.RollHoldTimer -= Time.unscaledDeltaTime;
                }
                else if (Mathf.Abs(pitch) < 70f)
                {
                    Quaternion gameNoRoll =
                        Quaternion.LookRotation(
                            gameRotation * Vector3.forward,
                            Vector3.up);

                    float gameRoll =
                        Mathf.DeltaAngle(
                            0f,
                            (Quaternion.Inverse(gameNoRoll) * gameRotation)
                                .eulerAngles.z);

                    current.LookRoll = Mathf.Clamp(gameRoll, -25f, 25f);
                }

                current.LookYawRelativeToBody = relativeYaw;
                current.LookPitch = pitch;
                current.HasLookOffset = true;

                Quaternion targetRotation = Quaternion.identity;
                Quaternion leadRotation = Quaternion.identity;

                float hipWeight = (1f - Mathf.Clamp01(current.AdsBlend)) * (1f - vanilla);
                float hipPitchOffset = Plugin.HipAimOffsetUp.Value * hipWeight;
                float hipYawOffset = Plugin.HipAimOffsetSide.Value * hipWeight;
                float hipRollOffset = -Plugin.HipAimOffsetRoll.Value * hipWeight;

                float cameraUp = Mathf.Lerp(
                    LimitCameraPitch(current.LookPitch + hipPitchOffset, true),
                    LimitCameraPitch(current.LookPitch + hipPitchOffset, false),
                    vanilla);

                {
                    float targetYaw =
                        bodyYaw +
                        current.LookYawRelativeToBody +
                        hipYawOffset;

                    targetRotation =
                        Quaternion.Euler(
                            -cameraUp,
                            targetYaw,
                            current.LookRoll * Plugin.LeanRollStrength.Value + hipRollOffset);

                    if (!current.HasInertialRotation)
                    {
                        current.InertialRotation =
                            targetRotation;

                        current.HasInertialRotation = true;
                        current.DeadYaw = targetYaw;
                        current.DeadPitch = cameraUp;
                        current.DeadLastYaw = targetYaw;
                        current.DeadLastPitch = cameraUp;
                        current.DeadStill = 0f;
                        current.LeadInertial = targetRotation;
                        current.GunDeadYaw = targetYaw;
                        current.GunDeadPitch = cameraUp;
                    }

                    DeadZoneMode dzMode = Plugin.DeadZoneModeCfg.Value;
                    float deadZone = (dzMode == DeadZoneMode.Camera ? Mathf.Max(0f, Plugin.CameraDeadZone.Value) : 0f) *
                                     (1f - Mathf.Clamp01(current.AdsBlend));
                    float dzDt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

                    float aimYawStep = Mathf.DeltaAngle(current.DeadLastYaw, targetYaw);
                    float aimPitchStep = cameraUp - current.DeadLastPitch;
                    float aimSpeed = Mathf.Sqrt(aimYawStep * aimYawStep + aimPitchStep * aimPitchStep) / dzDt;
                    current.DeadLastYaw = targetYaw;
                    current.DeadLastPitch = cameraUp;

                    float stillTarget = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 8f, aimSpeed));
                    current.DeadStill = Mathf.MoveTowards(
                        current.DeadStill,
                        stillTarget,
                        dzDt * (stillTarget > current.DeadStill ? 4f : 20f));

                    float dzYaw = Mathf.DeltaAngle(current.DeadYaw, targetYaw);
                    float dzPitch = cameraUp - current.DeadPitch;
                    float dzLen = Mathf.Sqrt(dzYaw * dzYaw + dzPitch * dzPitch);

                    if (dzLen > deadZone)
                    {
                        float dzK = (dzLen - deadZone) / dzLen;
                        current.DeadYaw += dzYaw * dzK;
                        current.DeadPitch += dzPitch * dzK;
                        dzYaw -= dzYaw * dzK;
                        dzPitch -= dzPitch * dzK;
                    }

                    float back = (1f - Mathf.Exp(-dzDt / 0.35f)) * current.DeadStill;
                    current.DeadYaw += dzYaw * back;
                    current.DeadPitch += dzPitch * back;

                    current.DeadYaw = Mathf.Repeat(current.DeadYaw, 360f);

                    {
                        float gunZone = (dzMode == DeadZoneMode.Weapon ? Mathf.Max(0f, Plugin.WeaponDeadZone.Value) : 0f) *
                                        (1f - Mathf.Clamp01(current.AdsBlend));

                        float gYaw = Mathf.DeltaAngle(current.GunDeadYaw, targetYaw);
                        float gPitch = cameraUp - current.GunDeadPitch;
                        float gLen = Mathf.Sqrt(gYaw * gYaw + gPitch * gPitch);

                        if (gLen > gunZone)
                        {
                            float gK = (gLen - gunZone) / gLen;
                            current.GunDeadYaw += gYaw * gK;
                            current.GunDeadPitch += gPitch * gK;
                            gYaw -= gYaw * gK;
                            gPitch -= gPitch * gK;
                        }

                        current.GunDeadYaw += gYaw * back;
                        current.GunDeadPitch += gPitch * back;
                        current.GunDeadYaw = Mathf.Repeat(current.GunDeadYaw, 360f);

                        Plugin.WeaponDeadOffset = new Vector2(
                            Mathf.DeltaAngle(targetYaw, current.GunDeadYaw),
                            current.GunDeadPitch - cameraUp);
                    }

                    Quaternion followRotation =
                        Quaternion.Euler(
                            -current.DeadPitch,
                            current.DeadYaw,
                            current.LookRoll * Plugin.LeanRollStrength.Value + hipRollOffset);

                    float rotationBlend = 1f - Mathf.Exp(
                        -Plugin.RotationInertia.Value *
                        Time.unscaledDeltaTime);

                    float thirdInertia = Mathf.Max(0f, Plugin.ThirdPersonInertia.Value) *
                                         (1f - Mathf.Clamp01(current.AdsBlend));
                    float thirdRotationBlend = thirdInertia <= 0.001f
                        ? 1f
                        : 1f - Mathf.Exp(-Time.unscaledDeltaTime / (thirdInertia * 0.01f));

                    rotationBlend = Mathf.Lerp(rotationBlend, thirdRotationBlend, third);

                    current.InertialRotation =
                        Quaternion.Slerp(
                            current.InertialRotation,
                            followRotation,
                            rotationBlend);

                    current.LeadInertial =
                        Quaternion.Slerp(
                            current.LeadInertial,
                            targetRotation,
                            rotationBlend);

                    leadRotation = targetRotation;
                }

                if (current.HasInertialRotation)
                {
                    Quaternion lead = Quaternion.FromToRotation(
                        current.LeadInertial * Vector3.forward,
                        leadRotation * Vector3.forward);

                    if (third > 0.0001f)
                        lead = Quaternion.Slerp(lead, Quaternion.identity, third);

                    Plugin.LeadLocal =
                        Quaternion.Inverse(current.InertialRotation) *
                        lead *
                        current.InertialRotation;
                    Plugin.HasLead = true;
                }
                else
                {
                    Plugin.HasLead = false;
                }

                Quaternion bodyCameraRotation =
                    current.HasInertialRotation
                        ? current.InertialRotation
                        : Quaternion.Euler(
                            -cameraUp,
                            bodyYaw +
                            current.LookYawRelativeToBody +
                            hipYawOffset,
                            current.LookRoll * Plugin.LeanRollStrength.Value + hipRollOffset);

                Vector3 shakePosition;
                float shakePitch;
                float shakeYaw;
                float shakeRoll;

                CalculateCameraShake(
                    current,
                    player,
                    playerTransform,
                    out shakePosition,
                    out shakePitch,
                    out shakeYaw,
                    out shakeRoll);

                shakeRoll += UpdateTurnRoll(current, gameYaw);

                SmoothMotion(
                    current,
                    ref shakePosition,
                    ref shakePitch,
                    ref shakeYaw,
                    ref shakeRoll);

                Vector3 shotReaction = EvaluateShotReaction(current);

                shakePitch += shotReaction.x;
                shakeYaw += shotReaction.y;
                shakeRoll += shotReaction.z;

                shakePitch += current.IdleShake.x;
                shakeYaw += current.IdleShake.y;
                shakeRoll += current.IdleShake.z;

                Vector3 impact = BodyCamHit.Evaluate();

                shakePitch += impact.x;
                shakeYaw += impact.y;
                shakeRoll += impact.z;

                Vector3 lookShake = BodyCamLookShake.Update(
                    gameYaw, cameraUp, current.SmoothedVelocity.magnitude,
                    Mathf.Max(0f, Plugin.LookShake.Value) *
                    Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(current.AdsBlend)));

                shakePitch += lookShake.x;
                shakeYaw += lookShake.y;
                shakeRoll += lookShake.z;

                float shakeK = Mathf.Lerp(1f, Mathf.Max(0f, Plugin.ThirdPersonShake.Value), third);

                shakePitch *= shakeK;
                shakeYaw *= shakeK;
                shakeRoll *= shakeK;
                shakePosition *= shakeK;

                Vector3 magLook = BodyCamMagCheck.LookOffset(bodyCameraRotation, current.CurrentPosition) *
                                  (1f - Mathf.Clamp01(current.AdsBlend)) * (1f - third);

                shakePitch += magLook.x;
                shakeYaw += magLook.y;

                Vector3 handling = BodyCamHandling.Evaluate(
                    (1f - Mathf.Clamp01(current.AdsBlend)) * (1f - third));

                shakePitch += handling.x;
                shakeYaw += handling.y;
                shakeRoll += handling.z;

                Vector3 adjust = BodyCamAdjust.Evaluate();

                shakePitch += adjust.x;
                shakeYaw += adjust.y;
                shakeRoll += adjust.z;

                shakePitch = Mathf.Clamp(shakePitch, cameraUp - 88f, cameraUp + 88f);

                Quaternion shakeRotation =
                    Quaternion.Euler(
                        shakePitch,
                        shakeYaw,
                        shakeRoll);

                bodyCameraRotation =
                    bodyCameraRotation * shakeRotation;

                current.CurrentPosition +=
                    bodyCameraRotation * shakePosition;

                current.CameraTransform.rotation =
                    bodyCameraRotation;

                current.CameraTransform.position =
                    current.CurrentPosition;

                if (current.ThirdBlend <= 0.001f)
                    BodyCamLeftHand.Apply(current.CameraTransform);

                BodyCamAimDot.Update(
                    player,
                    playerTransform,
                    current.CameraTransform,
                    current.ThirdBlend >= 0.999f && current.WasAiming);

                current.LogTimer +=
                    Time.unscaledDeltaTime;

                if (current.LogTimer >= 2f)
                {
                    current.LogTimer = 0f;

                    if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo(
                        $"[BodyCamera] ACTIVE " +
                        $"Anchor={current.AnchorName} " +
                        $"Camera={current.CameraTransform.name} " +
                        $"Normal={normalPosition} " +
                        $"TarkovADS={current.WasAiming} " +
                        $"Speed={current.SmoothedMoveSpeed:0.00} " +
                        $"VSpeed={current.SmoothedVerticalSpeed:0.00} " +
                        $"Current={current.CurrentPosition}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[BodyCamera] Runtime error: " + ex);
            }
        }

        private static void ResetRuntimeState(BodyCamState state)
        {
            state.HasCurrentPosition = false;
            state.HasChestLocal = false;
            state.HasChestRest = false;
            state.ChestPitch = 0f;
            state.HasInertialRotation = false;
            state.HasLookOffset = false;
            state.WasAiming = false;
            state.HasAimingState = false;
            state.AdsBlend = 0f;
            state.ThirdBlend = Plugin.ThirdPerson ? 1f : 0f;
            state.ThirdAdsCut = false;
            state.ThirdDist = -1f;
            state.ShoulderBlend = Plugin.ShoulderSwapped ? 1f : 0f;
            state.LastTarkovPosition = Vector3.zero;
            state.LastTarkovRotation = Quaternion.identity;
            state.HasLastTarkovState = false;
            state.StableAdsPosition = Vector3.zero;
            state.StableAdsRotation = Quaternion.identity;
            state.HasStableAdsState = false;
            state.AdsCandidatePosition = Vector3.zero;
            state.AdsCandidateRotation = Quaternion.identity;
            state.AdsCandidateTimer = 0f;
            state.HasAdsCandidate = false;
            state.AdsExitPosition = Vector3.zero;
            state.AdsExitNormalPosition = Vector3.zero;
            state.HasAdsExitPosition = false;
            state.LastPlayerPosition = Vector3.zero;
            state.HasLastPlayerPosition = false;
            ClearSway(state);
            state.CurrentPosition = Vector3.zero;
            state.InertialRotation = Quaternion.identity;
            state.LookYawRelativeToBody = 0f;
            state.LookPitch = 0f;
            state.LookRoll = 0f;
            state.HasTurnYaw = false;
            state.TurnYawRate = 0f;
            state.TurnRoll = 0f;
            state.TurnRollVel = 0f;
            BodyCamLookShake.Reset();
            BodyCamHandling.Reset();
            state.RollHoldTimer = 0f;
            ClearReactions(state);
            state.LogTimer = 0f;
            state.NextResolveTime = 0f;
        }

        private static void ClearSway(BodyCamState state)
        {
            state.SmoothedMoveSpeed = 0f;
            state.SmoothedVerticalSpeed = 0f;
            state.SmoothedVelocity = Vector3.zero;
            state.StridePhase = 0f;
            state.StepIndex = 0;
            state.StepVariation = 1f;
            state.StepVariationTarget = 1f;
            state.SmoothedYawRate = 0f;
            state.TiltPitch = 0f;
            state.TiltRoll = 0f;
            state.IdleWeight = 0f;
            state.IdleShake = Vector3.zero;
            state.PendingSteps = 0;
            state.StepCount = 0;
            state.StepTime = 10f;
            state.StepInterval = StepTemplateDuration;
            state.StepAmp = 0f;
            state.StepDistance = 0f;
            state.StepFallback = false;
            state.StepPitchUp = 0f;
            state.StepYawRight = 0f;
            state.StepRollRight = 0f;
            state.StepPitchVel = 0f;
            state.StepYawVel = 0f;
            state.StepRollVel = 0f;
            state.SwayWeight = 0f;
        }

        private static void CalculateCameraShake(
            BodyCamState state,
            Player player,
            Transform playerTransform,
            out Vector3 position,
            out float pitch,
            out float yaw,
            out float roll)
        {
            position = Vector3.zero;
            pitch = 0f;
            yaw = 0f;
            roll = 0f;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            Vector3 bodyForward =
                Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up);

            if (bodyForward.sqrMagnitude < 0.0001f)
                bodyForward = Vector3.forward;

            bodyForward.Normalize();

            Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyForward);

            float bodyYaw = GetHorizontalYaw(bodyForward);

            if (!state.HasLastPlayerPosition)
            {
                state.LastPlayerPosition = playerTransform.position;
                state.HasLastPlayerPosition = true;
                state.LastBodyYaw = bodyYaw;
                ClearSway(state);
                return;
            }

            Vector3 delta =
                playerTransform.position - state.LastPlayerPosition;

            state.LastPlayerPosition = playerTransform.position;

            float yawRate =
                Mathf.Clamp(
                    Mathf.DeltaAngle(state.LastBodyYaw, bodyYaw) / dt,
                    -360f,
                    360f);

            state.LastBodyYaw = bodyYaw;

            float master =
                Mathf.Max(0f, Plugin.CameraShakeMultiplier.Value);

            if (master < 0.0001f)
            {
                ClearSway(state);
                return;
            }

            float rawVerticalSpeed = Mathf.Clamp(delta.y / dt, -20f, 20f);

            state.SmoothedVerticalSpeed =
                Mathf.Lerp(
                    state.SmoothedVerticalSpeed,
                    rawVerticalSpeed,
                    1f - Mathf.Exp(-6f * dt));

            delta.y = 0f;

            Vector3 rawVelocity = Vector3.ClampMagnitude(delta / dt, 8f);

            float velocityBlend = 1f - Mathf.Exp(-12f * dt);

            state.SmoothedVelocity =
                Vector3.Lerp(
                    state.SmoothedVelocity,
                    rawVelocity,
                    velocityBlend);

            state.SmoothedYawRate =
                Mathf.Lerp(
                    state.SmoothedYawRate,
                    yawRate,
                    1f - Mathf.Exp(-10f * dt));

            float speed = state.SmoothedVelocity.magnitude;
            state.SmoothedMoveSpeed = speed;

            float forwardSpeed =
                Vector3.Dot(state.SmoothedVelocity, bodyForward);

            float rightSpeed =
                Vector3.Dot(state.SmoothedVelocity, bodyRight);

            bool sprinting = SprintState.IsSprinting(player);

            float nodAds = Mathf.Clamp01(state.AdsBlend);
            float nodStrength =
                Mathf.Lerp(
                    Mathf.Max(0f, Plugin.StepNod.Value),
                    Mathf.Max(0f, Plugin.AdsStepNod.Value),
                    nodAds) * master;

            float walk01 = Mathf.InverseLerp(0.3f, 2.7f, speed);
            float run01 = Mathf.InverseLerp(2.7f, 5.5f, speed);

            float nodSize;

            if (sprinting)
                nodSize = 4.0f;
            else if (speed < 0.8f)
                nodSize = Mathf.Lerp(0f, 0.9f, Mathf.InverseLerp(0.3f, 0.8f, speed));
            else if (speed < 1.6f)
                nodSize = Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(0.8f, 1.6f, speed));
            else
                nodSize = Mathf.Lerp(1.5f, 2.2f, Mathf.InverseLerp(1.6f, 2.9f, speed));

            bool airborne =
                Mathf.Abs(state.SmoothedVerticalSpeed) >
                Mathf.Max(2.5f, 1.2f * speed);

            state.StepTime = Mathf.Min(state.StepTime + dt, 10f);

            bool newStep = false;

            float sinceRealStep = Time.unscaledTime - state.LastRealStepTime;

            float stepLength =
                Mathf.Lerp(0.55f, 0.95f, walk01) + 0.75f * run01;

            if (state.PendingSteps > 0)
            {
                state.PendingSteps = 0;
                state.LastRealStepTime = Time.unscaledTime;
                state.StepDistance = 0f;
                state.StepFallback = false;

                newStep = speed > 0.05f && state.StepTime > 0.15f;
            }
            else if (!airborne &&
                     speed > 0.4f &&
                     sinceRealStep > Mathf.Max(0.4f, 1.15f * state.StepInterval))
            {
                if (!state.StepFallback)
                {
                    state.StepFallback = true;

                    if (sinceRealStep < 2f)
                        state.StepDistance = stepLength;
                }

                state.StepDistance += speed * dt;

                if (state.StepDistance >= stepLength)
                {
                    state.StepDistance = Mathf.Min(state.StepDistance - stepLength, stepLength);
                    newStep = true;
                }
            }
            else if (speed <= 0.4f)
            {
                state.StepDistance = 0f;
                state.StepFallback = false;
            }

            if (newStep)
            {
                float interval = state.StepTime;

                if (state.StepCount > 0 && interval > 0.2f && interval < 1.2f)
                {
                    state.StepInterval =
                        Mathf.Lerp(state.StepInterval, interval, 0.5f);
                }

                state.StepCount++;
                state.StepTime = 0f;

                state.StepAmp = nodSize * UnityEngine.Random.Range(0.7f, 1.3f);

                float speedAmp = Mathf.Lerp(0.45f, 1f, walk01) + 0.4f * run01;

                state.StepSign = -state.StepSign;
                state.StepRollAmp =
                    speedAmp *
                    UnityEngine.Random.Range(0.8f, 1.2f) *
                    UnityEngine.Random.Range(0.75f, 1.25f);
            }

            _gunStepCount = state.StepCount;
            _gunStepInterval = state.StepInterval;
            _gunStepStamp = Time.time;

            float stepDuration =
                Mathf.Clamp(state.StepInterval, 0.25f, 0.9f);

            float u = state.StepCount > 0
                ? Mathf.Clamp01(state.StepTime / stepDuration)
                : 1f;

            float targetUp =
                SamplePhase(StepPitchUpCurve, u) / StepCurveSpan *
                state.StepAmp * nodStrength;

            float stepRollStrength =
                Mathf.Max(0f, Plugin.StepRollStrength.Value) * master;

            float targetRollRight =
                SamplePhase(StepRollRightCurve, u) *
                state.StepSign * state.StepRollAmp * stepRollStrength;

            float targetYawRight =
                SamplePhase(StepYawRightCurve, u) *
                state.StepSign * state.StepRollAmp * stepRollStrength;

            {
                const float freq = 12f;
                float w = 2f * Mathf.PI * freq;
                float k = w * w;
                float c = 2f * w;

                float sdt = Mathf.Min(dt, 0.1f);
                int n = Mathf.Clamp(Mathf.CeilToInt(sdt / 0.004f), 1, 25);
                float h = sdt / n;

                for (int i = 0; i < n; i++)
                {
                    state.StepPitchVel += (k * (targetUp - state.StepPitchUp) - c * state.StepPitchVel) * h;
                    state.StepPitchUp += state.StepPitchVel * h;

                    state.StepRollVel += (k * (targetRollRight - state.StepRollRight) - c * state.StepRollVel) * h;
                    state.StepRollRight += state.StepRollVel * h;

                    state.StepYawVel += (k * (targetYawRight - state.StepYawRight) - c * state.StepYawVel) * h;
                    state.StepYawRight += state.StepYawVel * h;
                }

                if (float.IsNaN(state.StepPitchUp) || float.IsNaN(state.StepPitchVel) ||
                    float.IsNaN(state.StepRollRight) || float.IsNaN(state.StepRollVel) ||
                    float.IsNaN(state.StepYawRight) || float.IsNaN(state.StepYawVel))
                {
                    state.StepPitchUp = state.StepRollRight = state.StepYawRight = 0f;
                    state.StepPitchVel = state.StepRollVel = state.StepYawVel = 0f;
                }
            }

            float swayTarget = airborne ? 0f : Mathf.InverseLerp(0.3f, 0.9f, speed);

            state.SwayWeight = Mathf.Lerp(
                state.SwayWeight,
                swayTarget,
                1f - Mathf.Exp(-(swayTarget > state.SwayWeight ? 3f : 5f) * dt));

            state.SwayTime = (state.SwayTime + dt) % 10000f;

            float side01 = speed > 0.4f
                ? Mathf.Clamp01((Mathf.Abs(rightSpeed) / speed - 0.3f) / 0.4f)
                : 0f;

            float back01 = speed > 0.4f
                ? Mathf.Clamp01((-forwardSpeed / speed - 0.3f) / 0.4f)
                : 0f;

            float rollSize = sprinting
                ? 0.40f
                : 0.45f * (1f + 0.35f * Mathf.Max(side01, back01));

            float yawSize = sprinting ? 0.55f : 0.30f;

            float adsBlend = Mathf.Clamp01(state.AdsBlend);

            float sway =
                Mathf.Max(0f, Plugin.RandomSway.Value) * master * state.SwayWeight;

            float st = state.SwayTime;

            float nRoll =
                0.75f * Noise(st * 6.5f, 0.37f + st * 0.013f) +
                0.45f * Noise(st * 10.0f, 7.71f + st * 0.011f);

            float nYaw =
                0.75f * Noise(st * 3.5f, 3.31f + st * 0.017f) +
                0.50f * Noise(st * 6.0f, 9.93f + st * 0.019f);

            float swayRoll =
                nRoll / 0.29f * rollSize * sway * Mathf.Lerp(1f, 0.1f, adsBlend);

            float swayYaw =
                nYaw / 0.28f * yawSize * sway * Mathf.Lerp(1f, 0.4f, adsBlend);

            float bobPitch = -state.StepPitchUp;
            float bobYaw = swayYaw + state.StepYawRight;
            float bobRoll = swayRoll - state.StepRollRight;

            Vector3 bobPosition = Vector3.zero;

            float tilt =
                Mathf.Max(0f, Plugin.BodyTiltStrength.Value) * master;

            float targetPitch =
                Mathf.Clamp(forwardSpeed, -2f, 5f) * 0.30f * tilt;

            float bank =
                Mathf.Rad2Deg *
                Mathf.Atan(
                    state.SmoothedYawRate * Mathf.Deg2Rad * forwardSpeed /
                    9.81f) *
                0.25f;

            float targetRoll =
                -(Mathf.Clamp(rightSpeed, -3f, 3f) * 0.8f +
                  Mathf.Clamp(bank, -5f, 5f)) * tilt;

            float tiltBlend = 1f - Mathf.Exp(-8f * dt);

            state.TiltPitch =
                Mathf.Lerp(state.TiltPitch, targetPitch, tiltBlend);

            state.TiltRoll =
                Mathf.Lerp(state.TiltRoll, targetRoll, tiltBlend);

            float idleTarget = airborne ? 0f : 1f - Mathf.InverseLerp(0.05f, 0.4f, speed);

            if (Plugin.IdleShakeInMotion.Value)
                idleTarget = airborne ? 0f : 1f;

            float idleRate = idleTarget > state.IdleWeight ? 1.2f : 6f;

            state.IdleWeight =
                Mathf.Lerp(
                    state.IdleWeight,
                    idleTarget,
                    1f - Mathf.Exp(-idleRate * dt));

            state.IdleTime = (state.IdleTime + dt) % 10000f;

            float idle = Mathf.Max(0f, Plugin.IdleShake.Value) * master * state.IdleWeight *
                         Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(state.AdsBlend));

            if (idle > 0.0001f)
            {
                float t = state.IdleTime;

                float idleNPitch = (1.00f * Noise(t * 0.28f, 11.3f) +
                                0.55f * Noise(t * 1.10f, 23.7f) +
                                0.30f * Noise(t * 3.20f, 35.1f)) / (0.33f * 1.180f);

                float idleNYaw = (1.00f * Noise(t * 0.24f, 47.1f) +
                              0.55f * Noise(t * 1.00f, 59.9f) +
                              0.30f * Noise(t * 3.00f, 61.3f)) / (0.33f * 1.180f);

                float idleNRoll = (0.35f * Noise(t * 0.35f, 83.7f) +
                               1.00f * Noise(t * 1.00f, 95.3f) +
                               0.45f * Noise(t * 2.60f, 17.9f)) / (0.33f * 1.151f);

                state.IdleShake = new Vector3(
                    idleNPitch * 0.28f * idle,
                    idleNYaw * 0.23f * idle,
                    idleNRoll * 0.15f * idle);
            }
            else
            {
                state.IdleShake = Vector3.zero;
            }

            position = bobPosition;
            pitch = bobPitch + state.TiltPitch;
            yaw = bobYaw;
            roll = bobRoll + state.TiltRoll;
        }

        private static readonly RaycastHit[] _thirdHits = new RaycastHit[16];
        private static int _thirdMask;
        private static bool _thirdMaskLookup;

        private static int ThirdMask()
        {
            if (_thirdMaskLookup)
                return _thirdMask;

            _thirdMaskLookup = true;
            _thirdMask = Physics.DefaultRaycastLayers;

            try
            {
                var type = typeof(Player).Assembly.GetType("LayersMaskController");
                var field = type != null
                    ? type.GetField("HighPolyWithTerrainMask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                object value = field != null ? field.GetValue(null) : null;

                if (value is LayerMask)
                    _thirdMask = ((LayerMask)value).value;
                else if (value is int)
                    _thirdMask = (int)value;
            }
            catch
            {
            }

            return _thirdMask;
        }

        private static Vector3 ThirdPersonPosition(
            BodyCamState current, Transform self, Vector3 pivot, Vector3 aimForward, float dt)
        {
            Vector3 look = current.HasInertialRotation
                ? current.InertialRotation * Vector3.forward
                : aimForward;

            Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);

            if (flat.sqrMagnitude < 0.0001f)
                flat = aimForward;

            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(look.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -70f, 70f);

            Quaternion orbit = Quaternion.Euler(-pitch, yaw, 0f);

            current.ShoulderBlend = Mathf.MoveTowards(
                current.ShoulderBlend,
                Plugin.ShoulderSwapped ? 1f : 0f,
                Time.unscaledDeltaTime / 0.3f);

            float side = 1f - 2f * Mathf.SmoothStep(0f, 1f, current.ShoulderBlend);

            Vector3 offset = new Vector3(
                Plugin.ThirdPersonX.Value * side,
                Plugin.ThirdPersonY.Value,
                Plugin.ThirdPersonZ.Value);

            Vector3 delta = orbit * offset;
            float full = delta.magnitude;

            if (full < 0.001f)
                return pivot;

            Vector3 dir = delta / full;
            float allowed = full;
            const float radius = 0.12f;

            int n = Physics.SphereCastNonAlloc(
                pivot, radius, dir, _thirdHits, full, ThirdMask(), QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Collider col = _thirdHits[i].collider;

                if (col == null || (self != null && col.transform.IsChildOf(self)))
                    continue;

                if (_thirdHits[i].distance <= 0f)
                    continue;

                allowed = Mathf.Min(allowed, Mathf.Max(0.05f, _thirdHits[i].distance - 0.03f));
            }

            if (current.ThirdDist < 0f || allowed < current.ThirdDist)
                current.ThirdDist = allowed;
            else
                current.ThirdDist = Mathf.Lerp(current.ThirdDist, allowed, 1f - Mathf.Exp(-5f * dt));

            return pivot + dir * current.ThirdDist;
        }

        private static Vector3 FlatFacing(Quaternion rotation)
        {
            Vector3 f = rotation * Vector3.forward;
            Vector3 u = rotation * Vector3.up;

            Vector3 facing =
                Vector3.ProjectOnPlane(f, Vector3.up) -
                f.y * Vector3.ProjectOnPlane(u, Vector3.up);

            if (facing.sqrMagnitude < 0.000001f)
                return Vector3.zero;

            return facing.normalized;
        }

        private static Quaternion ChestFrame(BodyCamState current, Quaternion aimYaw, float dt)
        {
            Quaternion rel = Quaternion.Inverse(aimYaw) * current.Anchor.rotation;

            if (!current.HasChestRest)
            {
                current.ChestRest = rel;
                current.HasChestRest = true;
                current.ChestPitch = 0f;
            }
            else if (Mathf.Abs(current.LookPitch) < 10f && !current.WasAiming)
            {
                current.ChestRest = Quaternion.Slerp(
                    current.ChestRest, rel, 1f - Mathf.Exp(-2f * dt));
            }

            Vector3 f = (rel * Quaternion.Inverse(current.ChestRest)) * Vector3.forward;
            float down = Mathf.Atan2(-f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;
            down = Mathf.Clamp(down, -45f, 60f);

            current.ChestPitch = Mathf.Lerp(
                current.ChestPitch, down, 1f - Mathf.Exp(-dt / 0.05f));

            return aimYaw * Quaternion.Euler(current.ChestPitch, 0f, 0f);
        }

        private const float MotionSmoothMax = 0.14f;

        private static void SmoothMotion(
            BodyCamState current,
            ref Vector3 position,
            ref float pitch,
            ref float yaw,
            ref float roll)
        {
            float k = Plugin.MotionSmoothing != null
                ? Mathf.Clamp(Plugin.MotionSmoothing.Value, 0f, 2f)
                : 0f;

            Vector3 rot = new Vector3(pitch, yaw, roll);

            if (k <= 0.001f || !current.HasSmoothMotion)
            {
                current.SmoothRot1 = rot;
                current.SmoothRot2 = rot;
                current.SmoothPos1 = position;
                current.SmoothPos2 = position;
                current.HasSmoothMotion = true;
                return;
            }

            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.1f);
            float tau = 0.5f * MotionSmoothMax * k;
            float a = 1f - Mathf.Exp(-dt / tau);

            current.SmoothRot1 = Vector3.Lerp(current.SmoothRot1, rot, a);
            current.SmoothRot2 = Vector3.Lerp(current.SmoothRot2, current.SmoothRot1, a);
            current.SmoothPos1 = Vector3.Lerp(current.SmoothPos1, position, a);
            current.SmoothPos2 = Vector3.Lerp(current.SmoothPos2, current.SmoothPos1, a);

            pitch = current.SmoothRot2.x;
            yaw = current.SmoothRot2.y;
            roll = current.SmoothRot2.z;
            position = current.SmoothPos2;
        }

        private static float LimitCameraPitch(float up, bool lookDownLimit)
        {
            if (lookDownLimit)
            {
                float downLimit = Mathf.Clamp(Plugin.CameraLookDownLimit.Value, -85f, -30f);
                float downKnee = downLimit + 15f;

                if (up < downKnee)
                    up = downKnee - 15f * (1f - Mathf.Exp(-(downKnee - up) / 15f));
            }
            else if (up < -75f)
            {
                up = -75f - 10f * (1f - Mathf.Exp(-(-75f - up) / 10f));
            }

            if (up > 75f)
                up = 75f + 10f * (1f - Mathf.Exp(-(up - 75f) / 10f));

            return up;
        }

        private static float UpdateTurnRoll(BodyCamState state, float aimYaw)
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.1f);

            if (!state.HasTurnYaw)
            {
                state.HasTurnYaw = true;
                state.TurnLastYaw = aimYaw;
                state.TurnYawRate = 0f;
            }

            float rawRate = Mathf.Clamp(
                Mathf.DeltaAngle(state.TurnLastYaw, aimYaw) / dt, -720f, 720f);

            state.TurnLastYaw = aimYaw;
            state.TurnYawRate = Mathf.Lerp(
                state.TurnYawRate, rawRate, 1f - Mathf.Exp(-20f * dt));

            float strength =
                Mathf.Max(0f, Plugin.TurnRoll.Value) *
                Mathf.Max(0f, Plugin.CameraShakeMultiplier.Value) *
                Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(state.AdsBlend));

            float target =
                4.6f * (float)Math.Tanh(state.TurnYawRate / 140f) * strength;

            const float freq = 3.5f;
            const float zeta = 0.8f;
            float w = 2f * Mathf.PI * freq;
            float k = w * w;
            float c = 2f * zeta * w;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 25);
            float h = dt / n;

            for (int i = 0; i < n; i++)
            {
                state.TurnRollVel += (k * (target - state.TurnRoll) - c * state.TurnRollVel) * h;
                state.TurnRoll += state.TurnRollVel * h;
            }

            if (float.IsNaN(state.TurnRoll) || float.IsNaN(state.TurnRollVel))
            {
                state.TurnRoll = 0f;
                state.TurnRollVel = 0f;
            }

            return state.TurnRoll;
        }

        private static float Noise(float x, float y)
        {
            return (Mathf.PerlinNoise(x, y) - 0.5f) * 2f;
        }

        private static float GetHorizontalYaw(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return 0f;

            direction.Normalize();

            return Mathf.Atan2(
                direction.x,
                direction.z) * Mathf.Rad2Deg;
        }

        private static BodyCamState GetState(
            PlayerCameraController controller)
        {
            int id = controller.GetInstanceID();

            if (!States.TryGetValue(id, out var state))
            {
                PurgeDeadStates();

                state = new BodyCamState();
                state.Controller = controller;
                States[id] = state;
            }

            return state;
        }

        private static void PurgeDeadStates()
        {
            List<int> dead = null;

            foreach (var pair in States)
            {
                if (pair.Value == null || pair.Value.Controller == null)
                {
                    if (dead == null)
                        dead = new List<int>();

                    dead.Add(pair.Key);
                }
            }

            if (dead == null)
                return;

            for (int i = 0; i < dead.Count; i++)
                States.Remove(dead[i]);
        }

        private static bool Resolve(
            PlayerCameraController controller,
            BodyCamState state,
            bool forceLog)
        {
            var player =
                Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null)
                return false;

            state.LocalPlayerTransform =
                player.GetComponent<Transform>();

            if (PlayerBodyType == null)
            {
                if (!_warnedNoBodyType)
                {
                    _warnedNoBodyType = true;

                    Plugin.Log.LogError(
                        "[BodyCamera] EFT.PlayerBody type was not found.");
                }

                return false;
            }

            Component body =
                player.GetComponentInChildren(
                    PlayerBodyType,
                    true);

            if (body == null)
            {
                if (!_warnedBodyMissing)
                {
                    _warnedBodyMissing = true;

                    Plugin.Log.LogWarning(
                        "[BodyCamera] PlayerBody not found yet; waiting...");
                }

                return false;
            }

            if (Plugin.DebugLogging.Value && !_dumpedHierarchy)
            {
                _dumpedHierarchy = true;

                Plugin.Log.LogInfo(
                    $"[BodyCamera] PlayerBody found: " +
                    $"{body.transform.name}. " +
                    "Dumping transform hierarchy:");

                DumpHierarchy(
                    body.transform,
                    0,
                    250);
            }

            Transform anchor =
                FindBestBodyAnchor(body.transform);

            if (anchor == null)
            {
                if (forceLog || !_warnedNoAnchor)
                {
                    _warnedNoAnchor = true;

                    Plugin.Log.LogWarning(
                        "[BodyCamera] No suitable torso anchor found. " +
                        "See the hierarchy dump above.");
                }

                return false;
            }

            Camera camera =
                FindCamera(controller);

            if (camera == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;

                    Plugin.Log.LogWarning(
                        "[BodyCamera] Camera component not found " +
                        "under PlayerCameraController.");
                }

                return false;
            }

            state.Anchor = anchor;
            state.CameraTransform = camera.transform;
            state.AnchorName =
                GetRelativePath(
                    body.transform,
                    anchor);

            _warnedNoCamera = false;
            _warnedBodyMissing = false;
            _warnedNoAnchor = false;

            Plugin.Log.LogInfo(
                $"[BodyCamera] RESOLVED " +
                $"Body={body.transform.name} " +
                $"Anchor={state.AnchorName} " +
                $"Camera={camera.name}");

            return true;
        }

        private static readonly List<Camera> CameraBuffer =
            new List<Camera>(8);

        private static Camera FindCamera(
            PlayerCameraController controller)
        {
            controller.GetComponentsInChildren(true, CameraBuffer);

            if (CameraBuffer.Count == 0)
                return Camera.main;

            Camera mainCamera = Camera.main;

            if (mainCamera != null &&
                mainCamera.enabled &&
                CameraBuffer.Contains(mainCamera))
                return mainCamera;

            for (int i = 0; i < CameraBuffer.Count; i++)
            {
                Camera cam = CameraBuffer[i];

                if (cam != null && cam.enabled)
                    return cam;
            }

            return CameraBuffer[0];
        }

        private static Transform FindBestBodyAnchor(
            Transform root)
        {
            string[] preferred =
            {
                "Base HumanRibcage",
                "HumanRibcage",
                "Ribcage",

                "Chest",
                "UpperChest",
                "chest",
                "upperchest",

                "Spine3",
                "spine3",
                "Spine2",
                "spine2",
                "Spine1",
                "spine1",
                "Spine",
                "spine",

                "Torso",
                "torso",
                "UpperBody",
                "upperbody",
                "Body",
                "body"
            };

            foreach (string name in preferred)
            {
                Transform found =
                    FindTransformRecursive(
                        root,
                        name);

                if (found != null)
                    return found;
            }

            return FindTransformByKeyword(root);
        }

        private static Transform FindTransformByKeyword(
            Transform root)
        {
            Transform best = null;
            int bestScore = int.MinValue;

            var all =
                root.GetComponentsInChildren<Transform>(
                    true);

            foreach (var t in all)
            {
                if (t == null || t == root)
                    continue;

                string n =
                    t.name.ToLowerInvariant();

                int score = int.MinValue;

                if (n.Contains("ribcage"))
                    score = 120;
                else if (n.Contains("upperchest"))
                    score = 110;
                else if (n.Contains("chest"))
                    score = 100;
                else if (n.Contains("torso"))
                    score = 95;
                else if (n.Contains("spine3"))
                    score = 90;
                else if (n.Contains("spine2"))
                    score = 85;
                else if (n.Contains("spine1"))
                    score = 80;
                else if (n.Contains("spine"))
                    score = 70;
                else if (n.Contains("upperbody"))
                    score = 60;
                else if (n.Contains("body"))
                    score = 40;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }

            return best;
        }

        private static void DumpHierarchy(
            Transform root,
            int depth,
            int maxLines)
        {
            int emitted = 0;

            DumpHierarchyRecursive(
                root,
                depth,
                ref emitted,
                maxLines);

            if (emitted >= maxLines)
            {
                Plugin.Log.LogInfo(
                    $"[BodyCamera] Hierarchy dump stopped " +
                    $"at {maxLines} transforms.");
            }
        }

        private static void DumpHierarchyRecursive(
            Transform current,
            int depth,
            ref int emitted,
            int maxLines)
        {
            if (current == null ||
                emitted >= maxLines)
                return;

            string indent =
                new string(
                    ' ',
                    Math.Min(depth, 20) * 2);

            Plugin.Log.LogInfo(
                $"[BodyCamera] BONE {indent}{current.name}");

            emitted++;

            for (int i = 0;
                 i < current.childCount &&
                 emitted < maxLines;
                 i++)
            {
                DumpHierarchyRecursive(
                    current.GetChild(i),
                    depth + 1,
                    ref emitted,
                    maxLines);
            }
        }

        private static Transform FindTransformRecursive(
            Transform root,
            string targetName)
        {
            if (string.Equals(
                    root.name,
                    targetName,
                    StringComparison.OrdinalIgnoreCase))
                return root;

            for (int i = 0;
                 i < root.childCount;
                 i++)
            {
                Transform result =
                    FindTransformRecursive(
                        root.GetChild(i),
                        targetName);

                if (result != null)
                    return result;
            }

            return null;
        }

        private static string GetRelativePath(
            Transform root,
            Transform target)
        {
            var names =
                new List<string>();

            Transform current = target;

            while (current != null &&
                   current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();

            return string.Join(
                "/",
                names);
        }
    }

    internal sealed class BodyCamHitNotePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            foreach (MethodInfo m in typeof(Player).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "ApplyDamageInfo" && m.DeclaringType == typeof(Player))
                    return m;
            }

            return null;
        }

        [PatchPostfix]
        private static void Postfix(Player __instance)
        {
            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;

                if (mainPlayer != null && ReferenceEquals(mainPlayer, __instance))
                    BodyCamAdjust.NoteCombat();
            }
            catch
            {
            }
        }
    }

    internal sealed class BodyCamShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = typeof(EFT.Player.FirearmController);

            return type.GetMethod(
                "InitiateShot",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix(EFT.Player.FirearmController __instance)
        {
            var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;

            if (mainPlayer == null || mainPlayer.HandsController != __instance)
                return;

            BodyCamAdjust.NoteCombat();
            BodyCamRecoil.OnShot();
            BodyCamLateUpdatePatch.ApplyShotReaction();
            BodyCamRecoilProbe.OnShot(__instance, mainPlayer);
            BodyCamSounds.OnShot();
        }
    }

    internal sealed class BodyCamStepPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.Player).GetMethod(
                "PlayStepSound",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        [PatchPostfix]
        private static void Postfix(EFT.Player __instance)
        {
            if (__instance == null || !__instance.IsYourPlayer)
                return;

            BodyCamLateUpdatePatch.OnFootstep();
            BodyCamSounds.OnFootstep(__instance);
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? Browsable;

        public bool? IsAdvanced;
    }

    internal static class SprintState
    {
        private static Func<Player, bool> _getter;
        private static bool _lookedUp;

        internal static bool IsSprinting(Player player)
        {
            if (player == null)
                return false;

            if (!_lookedUp)
            {
                _lookedUp = true;

                try
                {
                    PropertyInfo prop = typeof(Player).GetProperty(
                        "IsSprintEnabled",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    MethodInfo get = prop != null ? prop.GetGetMethod(true) : null;

                    if (get != null && get.ReturnType == typeof(bool))
                        _getter = (Func<Player, bool>)Delegate.CreateDelegate(typeof(Func<Player, bool>), get);
                    else
                        Plugin.Log.LogWarning("[BodyCamera] Player.IsSprintEnabled not found; sprint is not checked for ADS.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[BodyCamera] Sprint check unavailable: " + ex.Message);
                }
            }

            if (_getter == null)
                return false;

            try
            {
                return _getter(player);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class ConfigMigration
    {
        private static ConfigFile _config;
        private static Dictionary<string, string> _fileValues;
        private static bool _changed;

        private static string Key(string section, string key)
        {
            return section + "\u0001" + key;
        }

        internal static void Begin(ConfigFile config)
        {
            _config = config;
            _fileValues = new Dictionary<string, string>();
            _changed = false;

            try
            {
                string path = config.ConfigFilePath;

                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    return;

                string section = "";

                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();

                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    int eq = line.IndexOf('=');

                    if (eq <= 0)
                        continue;

                    _fileValues[Key(section, line.Substring(0, eq).Trim())] =
                        line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Could not read old config: " + ex.Message);
            }
        }

        internal static ConfigEntry<T> Bind<T>(
            string section,
            string key,
            T defaultValue,
            string description,
            AcceptableValueBase range,
            int order,
            string oldSection,
            string oldKey,
            bool advanced = false)
        {
            bool alreadyInFile = _fileValues != null &&
                                 _fileValues.ContainsKey(Key(section, key));

            ConfigEntry<T> entry = _config.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(
                    description,
                    range,
                    new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced ? true : (bool?)null, Browsable = false }));

            BodyCamMenu.Register(section, key, order, entry);

            string oldValue;

            if (!alreadyInFile &&
                oldSection != null &&
                _fileValues != null &&
                _fileValues.TryGetValue(Key(oldSection, oldKey), out oldValue))
            {
                try
                {
                    entry.SetSerializedValue(oldValue);
                    _changed = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "[BodyCamera] Could not migrate " + oldSection + "/" + oldKey + ": " + ex.Message);
                }
            }

            return entry;
        }

        internal static void End()
        {
            try
            {
                var prop = typeof(ConfigFile).GetProperty(
                    "OrphanedEntries",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var orphans = prop != null
                    ? prop.GetValue(_config, null) as Dictionary<ConfigDefinition, string>
                    : null;

                if (orphans != null && orphans.Count > 0)
                {
                    var stale = new List<ConfigDefinition>();

                    foreach (var pair in orphans)
                    {
                        string sec = pair.Key.Section;

                        if (sec == "BodyCam" || sec == "Reactions" || sec == "Weapon" ||
                            sec == "Wind" || sec == "Debug" ||
                            (sec.Length > 2 && char.IsDigit(sec[0]) && sec[1] == '.'))
                            stale.Add(pair.Key);
                    }

                    for (int i = 0; i < stale.Count; i++)
                        orphans.Remove(stale[i]);

                    if (stale.Count > 0)
                        _changed = true;
                }

                if (_changed)
                {
                    _config.Save();
                    Plugin.Log.LogInfo("[BodyCamera] Settings file updated (old entries removed, values kept).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[BodyCamera] Config cleanup skipped: " + ex.Message);
            }

            _fileValues = null;
        }
    }
}

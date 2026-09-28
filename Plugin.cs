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
    [BepInPlugin("BodyCamera", "BodyCamera", "0.2.8")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey;

        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;
        internal static ConfigEntry<float> OffsetZ;

        internal static ConfigEntry<bool> KeepCameraRotation;
        internal static ConfigEntry<float> RotationInertia;
        internal static ConfigEntry<float> AdsInertia;
        internal static ConfigEntry<float> AdsCameraDistance;
        internal static ConfigEntry<float> AdsExitInertia;
        internal static ConfigEntry<float> CameraShakeMultiplier;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "BodyCam",
                "Enabled",
                false,
                "Enable the body-mounted camera test.");

            ToggleKey = Config.Bind(
                "BodyCam",
                "Toggle Key",
                KeyCode.F10,
                "Toggle BodyCam.");

            OffsetX = Config.Bind(
                "BodyCam",
                "Offset X",
                0.00f,
                new ConfigDescription(
                    "Normal camera position: left/right relative to the player body.",
                    new AcceptableValueRange<float>(-5.00f, 5.00f)));

            OffsetY = Config.Bind(
                "BodyCam",
                "Offset Y",
                0.08f,
                new ConfigDescription(
                    "Normal camera position: up/down relative to the player body.",
                    new AcceptableValueRange<float>(-5.00f, 5.00f)));

            OffsetZ = Config.Bind(
                "BodyCam",
                "Offset Z",
                0.08f,
                new ConfigDescription(
                    "Normal camera position: forward/back relative to the player body.",
                    new AcceptableValueRange<float>(-5.00f, 5.00f)));

            KeepCameraRotation = Config.Bind(
                "BodyCam",
                "Keep Camera Rotation",
                true,
                "Keep mouse-look while making camera rotation follow the player body.");

            RotationInertia = Config.Bind(
                "BodyCam",
                "Rotation Inertia",
                8.00f,
                new ConfigDescription(
                    "How quickly the camera follows body/mouse rotation. Higher = faster.",
                    new AcceptableValueRange<float>(0.1f, 30.0f)));

            AdsInertia = Config.Bind(
                "BodyCam",
                "ADS Inertia",
                8.00f,
                new ConfigDescription(
                    "How quickly the camera position transitions into standard Tarkov ADS. Higher = faster. Rotation inertia is controlled separately by Rotation Inertia.",
                    new AcceptableValueRange<float>(0.1f, 30.0f)));

            AdsExitInertia = Config.Bind(
                "BodyCam",
                "ADS Exit Inertia",
                8.00f,
                new ConfigDescription(
                    "How quickly the camera leaves standard Tarkov ADS. Higher = faster, lower = smoother.",
                    new AcceptableValueRange<float>(0.1f, 30.0f)));

            AdsCameraDistance = Config.Bind(
                "BodyCam",
                "ADS Camera Distance",
                0.00f,
                new ConfigDescription(
                    "Moves the BodyCam position along the ADS camera forward axis. Positive = farther back from the sight, negative = closer to the sight.",
                    new AcceptableValueRange<float>(-1.00f, 1.00f)));

            CameraShakeMultiplier = Config.Bind(
                "BodyCam",
                "Camera Shake Multiplier",
                1.00f,
                new ConfigDescription(
                    "Strength of movement camera shake. 0 = off, 1 = normal, higher = stronger.",
                    new AcceptableValueRange<float>(0.00f, 5.00f)));

            new BodyCamLateUpdatePatch().Enable();

            Log.LogInfo(
                "[BodyCamera] Loaded 0.2.8. Added movement camera shake with adjustable multiplier. RMB toggles standard Tarkov ADS with smooth camera inertia and smooth ADS exit. Press F10 to toggle. Default is OFF.");
        }

        internal static bool ToggleRequested()
        {
            return Input.GetKeyDown(ToggleKey.Value);
        }
    }

    internal sealed class BodyCamState
    {
        internal bool Active;
        internal Transform Anchor;
        internal Transform CameraTransform;
        internal Transform LocalPlayerTransform;
        internal string AnchorName;
        internal float LogTimer;

        internal Vector3 CurrentPosition;
        internal bool HasCurrentPosition;

        internal Quaternion InertialRotation;
        internal bool HasInertialRotation;

        // Captures the camera's normal mouse-look relative to the player's
        // horizontal body direction. This is what stops the body from orbiting
        // around a camera whose rotation is independent of the player.
        internal float LookYawRelativeToBody;
        internal float LookPitch;
        internal bool HasLookOffset;

        // RMB is used as a TOGGLE in Tarkov. This state mirrors that toggle
        // only for the BodyCam camera transition; Tarkov still owns the
        // actual weapon/ADS state.
        internal bool AdsTarget;
        internal float AdsBlend;
        internal bool WasSprinting;
        internal bool SprintRmbGuard;

        // AdsBlend is positional ADS-entry smoothing only. Rotation inertia
        // is handled independently by InertialRotation/RotationInertia.

        // Tarkov's camera transform must survive between frames.
        // BodyCam overwrites CameraTransform in Postfix, so before Tarkov's
        // next LateUpdate we restore the last real Tarkov camera state.
        internal Vector3 LastTarkovPosition;
        internal Quaternion LastTarkovRotation;
        internal bool HasLastTarkovState;

        // Stable ADS camera target. We do not feed a transient camera jump
        // caused by optic/reticle/magnification state changes directly into
        // the BodyCam position.
        internal Vector3 StableAdsPosition;
        internal Quaternion StableAdsRotation;
        internal bool HasStableAdsState;

        // Large one-frame jumps are held as a candidate and accepted only if
        // Tarkov keeps the new camera state for a short time. This filters the
        // transient frame produced by optic/reticle/magnification changes
        // without permanently locking the camera to the old optic.
        internal Vector3 AdsCandidatePosition;
        internal Quaternion AdsCandidateRotation;
        internal float AdsCandidateTimer;
        internal bool HasAdsCandidate;

        // When leaving ADS, freeze the last real ADS camera position and blend
        // from it to the chest position. This prevents an optic switch made
        // after RMB-up from pulling the BodyCam forward.
        internal Vector3 AdsExitPosition;
        internal bool HasAdsExitPosition;

        // Movement camera shake runtime. This is procedural camera shake only;
        // it does not modify the chest anchor or body animation.
        internal Vector3 LastPlayerPosition;
        internal bool HasLastPlayerPosition;
        internal float SmoothedMoveSpeed;
        internal float ShakePhase;
    }

    internal sealed class BodyCamLateUpdatePatch : ModulePatch
    {
        private static readonly Dictionary<int, BodyCamState> States =
            new Dictionary<int, BodyCamState>();

        private static readonly Type PlayerBodyType =
            Type.GetType("EFT.PlayerBody, Assembly-CSharp");

        private static bool _warnedNoPlayerBody;
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

                // BodyCam replaced the camera transform during the previous
                // postfix. Restore Tarkov's last real camera state before
                // Tarkov's LateUpdate runs again. This lets Tarkov rebuild
                // the camera when the optic, side sight or magnification
                // changes instead of feeding our own BodyCam transform back
                // into the controller.
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
                if (Plugin.ToggleRequested())
                {
                    var state = GetState(__instance);

                    state.Active = !state.Active;

                    Plugin.Log.LogInfo(
                        "[BodyCamera] " +
                        (state.Active ? "ENABLED" : "DISABLED"));

                    if (state.Active)
                    {
                        ResetRuntimeState(state);
                        Resolve(__instance, state, true);
                    }
                }

                var current = GetState(__instance);

                if (!current.Active || !Plugin.Enabled.Value)
                    return;

                // Tarkov can switch the active camera object when changing
                // between the primary sight, secondary sight and other camera
                // states. If that happens, the old CameraTransform is no longer
                // the rendered camera. Rebind to the currently enabled camera
                // before applying BodyCam, otherwise the game falls back to
                // its standard camera while our inertia state remains alive.
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

                /*
                 * POSITION
                 *
                 * The anchor supplies the physical chest location.
                 * The axes come from the PLAYER, not from the animated ribcage.
                 * Therefore a ribcage roll/twist cannot rotate the offset around
                 * the camera.
                 */
                Vector3 bodyRight = playerTransform.right;

                Vector3 bodyForward =
                    Vector3.ProjectOnPlane(
                        playerTransform.forward,
                        Vector3.up);

                if (bodyForward.sqrMagnitude < 0.001f)
                    bodyForward = Vector3.forward;
                else
                    bodyForward.Normalize();

                Vector3 normalPosition =
                    current.Anchor.position
                    + bodyRight * Plugin.OffsetX.Value
                    + Vector3.up * Plugin.OffsetY.Value
                    + bodyForward * Plugin.OffsetZ.Value;

                /*
                 * STANDARD TARKOV ADS + INERTIA
                 *
                 * Tarkov's own camera/weapon code runs before this postfix.
                 * We capture that camera state first, then smoothly blend
                 * BodyCam <-> Tarkov camera.
                 *
                 * RMB is treated as a TOGGLE (GetMouseButtonDown), matching
                 * the user's Tarkov control setup. BodyCam does not alter
                 * weapon ADS itself; Tarkov remains responsible for the real
                 * aiming state, zoom, sight picture, recoil, etc.
                 */
                Vector3 tarkovCameraPosition =
                    current.CameraTransform.position;

                Quaternion tarkovCameraRotation =
                    current.CameraTransform.rotation;

                // Save Tarkov's RAW camera state before applying our BodyCam-only
                // ADS distance offset. Prefix restores this exact state next frame.
                current.LastTarkovPosition = tarkovCameraPosition;
                current.LastTarkovRotation = tarkovCameraRotation;
                current.HasLastTarkovState = true;

                // User-adjustable ADS camera distance. This is applied only
                // while calculating the ADS target position, so normal BodyCam
                // distance and rotational inertia remain untouched.
                tarkovCameraPosition +=
                    -(tarkovCameraRotation * Vector3.forward) *
                    Plugin.AdsCameraDistance.Value;

                bool isSprinting =
                    (Input.GetKey(KeyCode.LeftShift) ||
                     Input.GetKey(KeyCode.RightShift)) &&
                    (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
                     Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f);

                // Tarkov cancels ADS when the player starts sprinting.
                // BodyCam previously kept its own RMB-toggle state as ADS=true,
                // so sprinting could leave us blending against a camera state
                // that Tarkov had already changed. That made the optic/sight
                // problem appear to move between the primary and secondary sight.
                // Mirror the sprint cancellation here, but do not otherwise
                // change the user's toggle-based ADS behavior.
                if (isSprinting && !current.WasSprinting && current.AdsTarget)
                {
                    // Sprint makes Tarkov leave ADS immediately. Do NOT use
                    // Tarkov's new sprint camera as the starting point for the
                    // BodyCam exit. Freeze the exact BodyCam position that was
                    // rendered on the previous frame, then blend back to the
                    // chest position. This prevents the one-frame reset to the
                    // vanilla FPS camera when W+Shift starts.
                    Vector3 sprintExitPosition = current.HasCurrentPosition
                        ? current.CurrentPosition
                        : normalPosition;

                    current.AdsTarget = false;
                    current.AdsExitPosition = sprintExitPosition;
                    current.HasAdsExitPosition = true;
                    current.AdsBlend = 1f;
                    current.CurrentPosition = sprintExitPosition;

                    Plugin.Log.LogInfo(
                        "[BodyCamera] Sprint started -> preserving BodyCam position and synchronizing ADS exit.");
                }

                current.WasSprinting = isSprinting;

                // Ctrl+RMB and Alt+RMB are Tarkov optic controls, not ADS.
                // Unity still reports RMB as pressed for modifier+RMB, so a
                // plain GetMouseButtonDown(1) would incorrectly toggle our
                // BodyCam ADS state and fight Tarkov's sight-switch logic.
                bool opticModifierHeld =
                    Input.GetKey(KeyCode.LeftControl) ||
                    Input.GetKey(KeyCode.RightControl) ||
                    Input.GetKey(KeyCode.LeftAlt) ||
                    Input.GetKey(KeyCode.RightAlt);

                bool adsTogglePressed =
                    Input.GetMouseButtonDown(1) && !opticModifierHeld;

                // If RMB is pressed while sprinting, Tarkov may temporarily
                // move/reset the FPS camera even though ADS cannot actually
                // start. Keep the BodyCam on the position/rotation rendered
                // immediately before the click instead of accepting that
                // transient sprint/ADS camera state.
                current.SprintRmbGuard = isSprinting && adsTogglePressed;

                if (adsTogglePressed)
                {
                    // Tarkov does not enter ADS while sprinting. Do not let
                    // BodyCam's RMB toggle enter its own ADS state either.
                    // Otherwise the live sprint camera becomes the ADS target
                    // and the BodyCam snaps away from the chest position.
                    if (isSprinting)
                    {
                        current.AdsTarget = false;
                        current.HasAdsExitPosition = false;
                        current.AdsBlend = 0f;
                    }
                    else
                    {
                        bool wasAds = current.AdsTarget;
                        current.AdsTarget = !current.AdsTarget;

                    if (!wasAds && current.AdsTarget)
                    {
                        // Entering ADS: start the smooth transition from the
                        // current BodyCam position. AdsBlend controls ADS entry.
                        current.AdsBlend = 0f;
                        current.HasAdsExitPosition = false;
                    }
                        else if (wasAds && !current.AdsTarget)
                        {
                            // Leaving ADS is now a smooth positional transition.
                            // Freeze the last BodyCam ADS position and blend it back
                            // to the chest position. This also prevents a simultaneous
                            // optic/reticle change from snapping the camera forward.
                            current.AdsExitPosition = current.CurrentPosition;
                            current.HasAdsExitPosition = true;
                            current.AdsBlend = 1f;
                        }
                    }
                }

                // IMPORTANT:
                // Do not maintain a second "stable" ADS camera target here.
                // Tarkov's live CameraTransform is the authoritative target for
                // the current optic/reticle/magnification. Keeping a filtered
                // copy here causes scope/side-sight changes to fight the real
                // Tarkov camera and can pull the BodyCam away from the sight.
                //
                // During ADS entry AdsBlend provides the desired inertia. Once
                // fully in ADS, the live Tarkov camera is used directly so
                // changing optic or magnification stays synchronized.

                if (current.AdsTarget)
                {
                    float adsBlendStep =
                        1f - Mathf.Exp(
                            -Plugin.AdsInertia.Value *
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
                            -Plugin.AdsExitInertia.Value *
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

                if (current.SprintRmbGuard)
                {
                    // RMB during an already-active sprint is not an ADS
                    // transition. Preserve the BodyCam frame from before the
                    // click so Tarkov cannot expose its transient camera reset.
                    targetPosition = current.HasCurrentPosition
                        ? current.CurrentPosition
                        : normalPosition;
                }
                else if (current.AdsTarget)
                {
                    // ADS entry keeps the smooth transition. After the blend
                    // reaches 1, Tarkov's live camera becomes the exact target,
                    // including side-sight and magnification changes.
                    if (current.AdsBlend > 0.995f)
                    {
                        // Once fully in ADS, do not keep interpolating the camera.
                        // Use Tarkov's live camera position directly so switching
                        // between primary/secondary sights and changing magnification
                        // can move the camera immediately with Tarkov's own ADS state.
                        targetPosition = tarkovCameraPosition;
                    }
                    else
                    {
                        targetPosition =
                            Vector3.Lerp(
                                normalPosition,
                                tarkovCameraPosition,
                                current.AdsBlend);
                    }
                }
                else if (current.HasAdsExitPosition)
                {
                    // During ADS exit, use the frozen ADS position as the start
                    // point. The live Tarkov camera is deliberately ignored here.
                    targetPosition =
                        Vector3.Lerp(
                            normalPosition,
                            current.AdsExitPosition,
                            current.AdsBlend);
                }
                else
                {
                    targetPosition = normalPosition;
                }

                current.CurrentPosition = targetPosition;
                current.HasCurrentPosition = true;

                /*
                 * ROTATION
                 *
                 * Do NOT simply keep the old world camera rotation.
                 * That makes the player body rotate around an independently
                 * oriented camera.
                 *
                 * Instead:
                 *   1. Read the normal game camera's mouse-look.
                 *   2. Calculate its yaw relative to the player's body yaw.
                 *   3. Rebuild the camera rotation from the player's body yaw
                 *      plus that relative look.
                 *   4. Force roll to zero.
                 *
                 * This keeps the camera physically tied to the body while
                 * retaining normal mouse look.
                 */
                Quaternion gameRotation =
                    current.SprintRmbGuard && current.HasInertialRotation
                        ? current.InertialRotation
                        : tarkovCameraRotation;

                float bodyYaw =
                    GetHorizontalYaw(bodyForward);

                float gameYaw =
                    GetHorizontalYaw(
                        Vector3.ProjectOnPlane(
                            gameRotation * Vector3.forward,
                            Vector3.up));

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

                // Preserve the current game's mouse look relative to body.
                current.LookYawRelativeToBody = relativeYaw;
                current.LookPitch = pitch;
                current.HasLookOffset = true;

                Quaternion targetRotation;

                if (Plugin.KeepCameraRotation.Value)
                {
                    float targetYaw =
                        bodyYaw +
                        current.LookYawRelativeToBody;

                    targetRotation =
                        Quaternion.Euler(
                            -current.LookPitch,
                            targetYaw,
                            0f);

                    if (!current.HasInertialRotation)
                    {
                        current.InertialRotation =
                            targetRotation;

                        current.HasInertialRotation = true;
                    }

                    float rotationBlend =
                        1f - Mathf.Exp(
                            -Plugin.RotationInertia.Value *
                            Time.unscaledDeltaTime);

                    current.InertialRotation =
                        Quaternion.Slerp(
                            current.InertialRotation,
                            targetRotation,
                            rotationBlend);

                    // Keep the smoothed BodyCam rotation.
                }
                else
                {
                    current.HasInertialRotation = false;
                }

                Quaternion bodyCameraRotation =
                    current.HasInertialRotation
                        ? current.InertialRotation
                        : Quaternion.Euler(
                            -current.LookPitch,
                            bodyYaw +
                            current.LookYawRelativeToBody,
                            0f);

                // Add small speed-dependent pitch/yaw shake without roll.
                // The original BodyCam rotation/inertia remains the base.
                Vector3 shakePosition;
                float shakePitch;
                float shakeYaw;

                CalculateCameraShake(
                    current,
                    playerTransform,
                    out shakePosition,
                    out shakePitch,
                    out shakeYaw);

                Quaternion shakeRotation =
                    Quaternion.Euler(
                        shakePitch,
                        shakeYaw,
                        0f);

                bodyCameraRotation =
                    bodyCameraRotation * shakeRotation;

                // Apply the same local shake offset to the already-selected
                // BodyCam/ADS position. This does not change ADS target logic.
                current.CurrentPosition +=
                    bodyCameraRotation * shakePosition;

                /*
                 * ROTATION INERTIA IS INDEPENDENT FROM ADS ENTRY.
                 *
                 * This is intentional: RotationInertia is the same physical
                 * camera/hand lag in both normal BodyCam and ADS.
                 *
                 * AdsInertia controls ONLY the positional transition into ADS.
                 * It must not turn ADS rotation into an ordinary instant
                 * first-person camera once AdsBlend reaches 1.
                 *
                 * Tarkov still owns the real ADS state, sight alignment,
                 * recoil and FOV. We only follow its current desired
                 * rotation through the same inertial filter used by BodyCam.
                 */
                current.CameraTransform.rotation =
                    bodyCameraRotation;

                /*
                 * Position is written AFTER rotation so any camera controller
                 * change performed earlier in LateUpdate cannot leave the
                 * camera at the old position.
                 */
                current.CameraTransform.position =
                    current.CurrentPosition;

                current.LogTimer +=
                    Time.unscaledDeltaTime;

                if (current.LogTimer >= 2f)
                {
                    current.LogTimer = 0f;

                    Plugin.Log.LogInfo(
                        $"[BodyCamera] ACTIVE " +
                        $"Anchor={current.AnchorName} " +
                        $"Camera={current.CameraTransform.name} " +
                        $"Normal={normalPosition} " +
                        $"Target={current.AdsTarget} " +
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
            state.HasInertialRotation = false;
            state.HasLookOffset = false;
            state.AdsTarget = false;
            state.AdsBlend = 0f;
            state.WasSprinting = false;
            state.SprintRmbGuard = false;
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
            state.HasAdsExitPosition = false;
            state.LastPlayerPosition = Vector3.zero;
            state.HasLastPlayerPosition = false;
            state.SmoothedMoveSpeed = 0f;
            state.ShakePhase = 0f;
            state.CurrentPosition = Vector3.zero;
            state.InertialRotation = Quaternion.identity;
            state.LookYawRelativeToBody = 0f;
            state.LookPitch = 0f;
            state.LogTimer = 0f;
        }

        private static void CalculateCameraShake(
            BodyCamState state,
            Transform playerTransform,
            out Vector3 position,
            out float pitch,
            out float yaw)
        {
            position = Vector3.zero;
            pitch = 0f;
            yaw = 0f;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            if (!state.HasLastPlayerPosition)
            {
                state.LastPlayerPosition = playerTransform.position;
                state.HasLastPlayerPosition = true;
                state.SmoothedMoveSpeed = 0f;
                return;
            }

            Vector3 delta =
                playerTransform.position - state.LastPlayerPosition;

            state.LastPlayerPosition = playerTransform.position;

            // Ignore vertical displacement from jumps/steps when measuring
            // movement speed. The shake itself provides the vertical motion.
            delta.y = 0f;

            float rawSpeed = delta.magnitude / dt;

            // Prevent a teleport/spawn/physics spike from producing one huge
            // camera kick.
            rawSpeed = Mathf.Clamp(rawSpeed, 0f, 8f);

            float speedBlend =
                1f - Mathf.Exp(-10f * dt);

            state.SmoothedMoveSpeed =
                Mathf.Lerp(
                    state.SmoothedMoveSpeed,
                    rawSpeed,
                    speedBlend);

            float movement01 =
                Mathf.Clamp01(state.SmoothedMoveSpeed / 5.0f);

            float intensity =
                movement01 * movement01 *
                Mathf.Max(0f, Plugin.CameraShakeMultiplier.Value);

            if (intensity < 0.0001f)
                return;

            // Frequency rises with movement speed: walking is a slower,
            // softer shake; sprinting becomes quicker and more noticeable.
            float frequency =
                Mathf.Lerp(5.5f, 10.5f, movement01);

            state.ShakePhase +=
                dt * frequency;

            float t = state.ShakePhase;

            // Several incommensurate waves make the movement feel like
            // physical camera vibration instead of a clean sinusoidal bob.
            float vertical =
                (Mathf.Sin(t * 2.0f) * 0.70f +
                 Mathf.Sin(t * 4.35f + 0.7f) * 0.22f) *
                0.0060f * intensity;

            float lateral =
                (Mathf.Sin(t * 1.65f + 1.1f) * 0.55f +
                 Mathf.Sin(t * 3.75f + 2.4f) * 0.20f) *
                0.0045f * intensity;

            float forward =
                Mathf.Sin(t * 2.55f + 2.0f) *
                0.0020f * intensity;

            pitch =
                (Mathf.Sin(t * 2.15f + 0.4f) * 0.65f +
                 Mathf.Sin(t * 4.1f + 1.8f) * 0.18f) *
                0.75f * intensity;

            yaw =
                (Mathf.Sin(t * 1.8f + 2.2f) * 0.55f +
                 Mathf.Sin(t * 3.95f + 0.5f) * 0.16f) *
                0.45f * intensity;

            position =
                new Vector3(
                    lateral,
                    vertical,
                    forward);
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
                state = new BodyCamState();
                States[id] = state;
            }

            return state;
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
                if (!_warnedNoPlayerBody)
                {
                    _warnedNoPlayerBody = true;

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
                if (!_warnedNoPlayerBody)
                {
                    _warnedNoPlayerBody = true;

                    Plugin.Log.LogWarning(
                        "[BodyCamera] PlayerBody not found yet; waiting...");
                }

                return false;
            }

            if (!_dumpedHierarchy)
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
                if (forceLog || !_warnedNoPlayerBody)
                {
                    _warnedNoPlayerBody = true;

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

            Plugin.Log.LogInfo(
                $"[BodyCamera] RESOLVED " +
                $"Body={body.transform.name} " +
                $"Anchor={state.AnchorName} " +
                $"Camera={camera.name}");

            return true;
        }

        private static Camera FindCamera(
            PlayerCameraController controller)
        {
            Camera[] cameras =
                controller.GetComponentsInChildren<Camera>(true);

            if (cameras == null ||
                cameras.Length == 0)
                return Camera.main;

            // Prefer Unity's actual MainCamera when it belongs to this
            // controller. This is more reliable during optic/secondary-sight
            // camera swaps than simply taking the first enabled child.
            Camera mainCamera = Camera.main;

            if (mainCamera != null)
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] == mainCamera && mainCamera.enabled)
                        return mainCamera;
                }
            }

            foreach (var cam in cameras)
            {
                if (cam != null && cam.enabled)
                    return cam;
            }

            return cameras[0];
        }

        private static Transform FindBestBodyAnchor(
            Transform root)
        {
            // Central chest/ribcage first.
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
}

# BodyCamera 1.0.0

Body camera for SPT 4.1.6 (Escape from Tarkov single-player). Your view becomes a chest-mounted action camera: it sits on your chest, lags behind your turns, bobs with every step, jolts with every shot, and the picture looks like real body-cam footage. A third person camera and a camera on the weapon are included.

Author: RedSparrow

## Features

- Chest camera: own position for pistols, rotation inertia, dead zone (camera or weapon), soft limit for looking down; aiming and the gun keep working as usual.
- Camera motion: a nod and a roll on every step, random sway, idle shake, body tilt, roll in turns, shake while turning the view, two shot reactions (spring jolt and roll), impacts from hits, explosions and falls.
- Weapon: idle and walk motion (hip and aiming, by pace), spring, inertia, tilt at corners and door frames, raise while firing from the hip, muzzle shove at close range, camera follows the hands on reload, weapon inspect, chamber and magazine checks.
- Lens: fisheye, radial blur, image circle with a soft edge and a warm rim that moves with turns and steps, dirt on the glass. Works with DLSS / FSR.
- Overlay: two styles (time, battery, REC, PLAY / date, name, device line, icon), CAM CONNECTED, exact rounds left next to the magazine when you check it.
- On / off: a hand adjusts the camera with a short animation, a beep and the CAM CONNECTED screen; now and then the camera is readjusted on its own.
- Sounds: game sound through the camera microphone, breathing by stamina and wounds, wind on the mic, gear rubbing on turns, mount knocks on steps, mic rattle on shots.
- Body: third person over the shoulder with your body and gear, torso and armor below the chest camera, camera on the end of the barrel.
- Own settings menu (English / Russian), 10 profiles with load keys.
- Everything works only for your own character and only while BodyCamera is on.

## Requirements

- SPT 4.1.6

## Install

1. Extract the archive.
2. Copy the `BepInEx` folder into your SPT folder. You should get:
   `SPT\BepInEx\plugins\BodyCamera\BodyCamera.dll`
   `SPT\BepInEx\plugins\BodyCamera\sounds\...`
3. Start SPT. The settings file is created at `SPT\BepInEx\config\BodyCamera.cfg`.

## Update

Replace the `BodyCamera` folder in `SPT\BepInEx\plugins`. Your settings are kept; settings that no longer exist are removed from the file automatically.

## Uninstall

Delete `SPT\BepInEx\plugins\BodyCamera` and, if you like, `SPT\BepInEx\config\BodyCamera.cfg` and `BodyCamera_Profile*.cfg`.

## Controls

- `\` (Backslash): BodyCamera on / off.
- `F8`: settings menu (English / Russian).
- `Z`: third person on / off.
- `Left Ctrl`: other shoulder in third person.
- `F4`: camera on the weapon.
- All keys can be changed in the menu.

## Settings (menu tabs)

1. General: on / off, keys, menu colour, weapon camera, on / off animation, random readjust, profiles.
2. Camera Position: chest position, pistol position, hip aim offset (up, side, roll).
3. Camera Rotation: rotation inertia, dead zone, look down limit, lean roll.
4. Camera Motion: overall strength, smoothing, steps, sway, idle shake, turns, body tilt, shot reactions, impacts.
5. Weapon: idle and walk motion, spring, inertia, corner tilt, hip fire raise, muzzle shove, reload and check camera, magazine look.
6. Overlay: style, texts, name, device line, magazine count, size, curve, battery, sounds.
7. Lens: fisheye, radial blur, vignette, movement, dirt.
8. Sounds: camera microphone, breathing, wind, turns, steps, mic rattle.
9. Body: third person keys, position, inertia, shake, what is shown in first and third person.

## Compatibility and known issues

- Made and tested with Realism Stance Overhaul and Fontaine's FOV Fix. Use the game's FOV slider (or FOV Fix) for the field of view.
- Other mods that move the first-person camera every frame may conflict; turn `Keep Camera Rotation` off to get Tarkov's camera position back.
- The lens copies the final frame once per frame; on weak GPUs turning `Lens Effects` off gives some FPS back.
- In third person the arms come from the body model, so while aiming they may not match the weapon exactly.

## Credits

- VCR OSD Mono font by Riciery Leal (free font), used for the overlay.

## License

Same as BodyCamera 0.3.0.

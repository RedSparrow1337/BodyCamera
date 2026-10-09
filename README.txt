# BodyCamera 0.3.0

A standalone camera-behavior mod for SPT 4.1.6.

## Features

- Body-mounted first-person camera feel.
- Configurable camera position and movement behavior.
- Adjustable camera inertia while turning and aiming.
- Configurable movement camera shake.
- Camera-only shot reaction with separate vertical and horizontal strength controls.
- Smooth shot-kick recovery.
- F10 toggle; BodyCamera is disabled by default.
- Does not intentionally move or modify the weapon model for the shot-camera effect.

## Compatibility

- SPT 4.1.6

## Install (players)

1. Extract the release archive.
2. Copy the `BepInEx` folder into your SPT installation folder and allow the `plugins\BodyCamera\BodyCamera.dll` file to be placed at:
   `SPT\BepInEx\plugins\BodyCamera\BodyCamera.dll`
3. Launch SPT. The configuration file is generated at `SPT\BepInEx\config\BodyCamera.cfg`.
4. BodyCamera is disabled by default. Press F10 in game to toggle it on/off.

## Build from source

Requires .NET SDK and an SPT 4.1.6 installation.

From PowerShell in this project folder:

```powershell
.\build.ps1 -SptRoot "D:\SPT"
```

The script builds the Release DLL and copies it to `BepInEx\plugins\BodyCamera\BodyCamera.dll`. If the build fails, it will not replace the installed DLL.

## Configuration

After the first launch, edit `BepInEx\config\BodyCamera.cfg`.

- `Shot Kick Strength`: overall multiplier; `0` disables the shot kick.
- `Shot Kick Pitch Strength`: vertical camera kick.
- `Shot Kick Yaw Strength`: maximum horizontal camera kick magnitude.
- Camera movement shake has its own separate `Camera Shake Multiplier` setting.

## Known issues

This is an early release. Camera behavior and shot-kick tuning may be refined based on testing. Back up your existing DLL before replacing it.

## License

MIT. See `LICENSE`.

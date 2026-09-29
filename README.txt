BodyCamera 0.2.9

Based on the clean BodyCamera 0.2.9 ADS Stability Fix v4.

ADS state is now observed from Tarkov's live
Player.ProceduralWeaponAnimation.IsAiming state instead of maintaining a separate
BodyCamera RMB/ADS toggle.

Tarkov remains responsible for ADS, optics, magnification, recoil and weapon state.
BodyCamera only smooths the camera transition to/from the live Tarkov ADS camera.

Build:
dotnet build -c Release -p:SptRoot="D:\SPT"

Install:
Copy-Item ".\bin\Release\netstandard2.1\BodyCamera.dll" "D:\SPT\BepInEx\plugins\BodyCamera\BodyCamera.dll" -Force

# Sprocket Air Support

Scout and kamikaze drones for Sprocket, with mouse steering and separate launcher parts.

Requires **BepInEx 6 IL2CPP** and [SprocketKeybinds](https://github.com/RoanWassink/SprocketKeybinds) **0.1.6**.

## Install

Close the game. Extract the full mod ZIP beside `Sprocket.exe`, keeping both `BepInEx` and `Sprocket_Data`.
Fit a launcher under **Vehicle Designer > Firepower > Drones**. Mount its hatch level with the armour and leave room for it to open.

This repository contains the source. The DLL needs the part files, localization and icons from the full installation package.

## Controls

| Default key | Action |
| --- | --- |
| Alt + H | Launch or select scout |
| Alt + K | Launch or select kamikaze |
| Alt + J | Switch between tank and drone |
| Alt + G | Switch between orbit and mouse flight |
| Alt + C | Change camera |
| Alt + X | Scout: mark a point. Kamikaze: arm or disarm |
| Alt + L | Disarm and return to the tank |

Move the mouse to steer. Use the wheel to adjust speed, or zoom in the scout camera.
Rebind keys under **bachvnju > drone**. Change Modifier to Shift if you prefer Shift combinations.

Kamikaze launches armed. Alt + J restores your ARMED/SAFE choice when you return to it. Orbit and Return disarm the warhead.
Scout has 100 HP; kamikaze has 80 HP and 480 mm penetration. Both can be shot down.

## Launchers

Both hatches open 150 degrees. The lift defaults to 20 cm; set `RaisedHeight` between `0.10` and `0.25` in `BepInEx/config/sprocket.drones.cfg`.
Scout raises its rail, unfolds its wings, then launches from a moving sled. Kamikaze leaves the tube before unfolding.

## Build

Use .NET 6 SDK and the interop assemblies from your installed game:

```powershell
dotnet build src/SprockeAIrsupport/SprocketDrones.csproj -c Release -p:SprocketGameRoot="D:\SteamLibrary\steamapps\common\Sprocket"
```

Build output: `src/SprockeAIrsupport/bin/Release/net6.0/SprocketDrones.dll`.
Real tank damage, paint and launcher clearance still need testing on your vehicle.

[MIT license](src/LICENSE).

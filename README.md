# Sprocket Air Support

Scout and kamikaze drones for Sprocket.

Requires **BepInEx 6 IL2CPP** and [SprocketKeybinds](https://github.com/RoanWassink/SprocketKeybinds) **0.1.6**.

## Install

Close the game and extract the full mod ZIP beside `Sprocket.exe`.
Fit a launcher under **Firepower > Drones**, with its hatch level with the armour.

## Controls

| Key | Action |
| --- | --- |
| Alt + H | Launch or select scout |
| Alt + K | Launch or select kamikaze |
| Alt + J | Switch tank / drone |
| Alt + G | Toggle orbit |
| Alt + C | Change camera |
| Alt + X | Mark a point / toggle warhead |
| Alt + L | Disarm and return |

Mouse: steer. Wheel: speed / camera zoom. Rebind under **bachvnju > drone**.
Kamikaze starts armed. Alt + J preserves your arming choice; Orbit and Return disarm it.

## Build

Requires .NET 6 SDK and the game's interop assemblies.

```powershell
dotnet build src/SprockeAIrsupport/SprocketDrones.csproj -c Release -p:SprocketGameRoot="D:\SteamLibrary\steamapps\common\Sprocket"
```

[MIT license](src/LICENSE).

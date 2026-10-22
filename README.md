# SideLock

`SideLock` is a small Windows system-tray utility that disables the standard Back and Forward behavior of mouse side buttons in browsers, File Explorer, and other applications.

## The problem

Mouse side buttons normally send Windows **Back** (`Mouse4`) and **Forward** (`Mouse5`) commands. They are convenient Push-to-Talk buttons for Discord, but pressing them while the pointer is over a browser, File Explorer, or Discord can also cause unwanted navigation.

## How it works

| Mouse button | Normal Windows action | SideLock action when blocked | Discord key |
| --- | --- | --- | --- |
| `Mouse4` | Back | Blocks the button | `F23` |
| `Mouse5` | Forward | Blocks the button | `F24` |

Each button can be blocked independently. Clearing its checkbox restores the normal Windows behavior for that button.

## Usage

1. Run `SideLock.exe` from the `publish` folder.
2. Find the SideLock icon in the Windows notification area near the clock.
3. Right-click the icon.
4. Check `Block Mouse4` and/or `Block Mouse5`.

The blocking choices are saved between launches.

## Tray menu

| Item | Purpose |
| --- | --- |
| `Settings → Start with Windows` | Starts SideLock when you sign in to Windows. |
| `Block Mouse5` | Blocks Mouse5 and sends `F24`. |
| `Block Mouse4` | Blocks Mouse4 and sends `F23`. |
| `Exit` | Completely exits SideLock. |

## Important Discord limitation

SideLock blocks standard Windows mouse messages, so browsers, File Explorer, and most applications do not receive the navigation command. Discord can also read original mouse events using **Raw Input**. As a result, while recording a keybind Discord may label the first side button as `Mouse3`; this is Discord's naming convention, not a SideLock error.

If Discord uses Raw Input in your scenario, completely hiding the physical side button from Discord requires remapping the button in the mouse manufacturer's software or using a driver-level interceptor. SideLock will still block navigation in regular applications.

## Build

.NET SDK 8 or newer is required:

```powershell
dotnet publish .\SideLock.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

The built application is `publish\SideLock.exe`.

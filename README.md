# VitaPad

VitaPad is a portable Windows 11 x64 controller mapper with an integrated floating gamepad keyboard called PadBoard. It works with XInput controllers, including a Retroid Pocket exposed through Moonlight, Apollo, or Sunshine.

[Download the latest release](https://github.com/page-muncher/VitaPad/releases/latest)

## Start using VitaPad

Download and extract `VitaPad-v1.5.1-portable.zip`, then run `VitaPad.exe`. The runtime is included. The controller studio opens maximized, and mappings pause while it is visible. Close or minimize the studio, choose **To tray**, or hold controller **Start** for one second to enable your mappings. Tapping Start in the studio selects its binding.

Click the controller icon in the notification area, or launch VitaPad again, to reopen the running instance's studio. When upgrading from a build older than 1.5.1, exit the old instance from its tray menu once before launching the new version.

## Controller mapping

Each of three universal profiles has Base, L1, and R1 layers. Select a controller input and assign keys, keyboard chords, mouse buttons, wheel actions, or **PadBoard**. Multiple controls can share an output. Holds and repeats are supported.

Each stick supports **Keys**, **Pointer**, or **Scroll** mode per layer. Use Pointer mode and the pointer-speed setting for mouse movement. Stick dead zones and trigger thresholds are adjustable.

Hold L1 or R1 for that layer. Holding both bumpers uses Base. After half a second, a preview displays the assigned bindings. It uses 75% opacity and carries its countdown across bumper combinations. Using an assigned binding hides the preview until both bumpers are released.

The diagram uses PlayStation face-button positions. Cross is XInput A, Circle is B, Square is X, and Triangle is Y.

## PadBoard

A new configuration assigns **Base R3** in Profile 1 to PadBoard. Send VitaPad to the tray and focus the application where you want to type. Press R3 to open the keyboard, then press R3 again to return to mapping. Any input can be assigned the PadBoard binding type.

| Control | Action |
| --- | --- |
| D-pad | Move key highlight |
| Cross | Type selected key |
| Square | Backspace |
| Triangle | Space |
| Circle | Shift on QWERTY or alternate keys on Symbols |
| L1 | Switch layouts |
| L2 / R2 | Jump to left / right keyboard anchor |
| R1 + D-pad | Move caret |
| R1 + L1 + D-pad | Extend selection |
| R1 + Square | Ctrl+Backspace to delete the previous word |
| R1 + Cross | Enter |
| R1 + Triangle | Copy |
| R1 + L1 + Triangle | Paste |
| R1 + Circle | Close PadBoard |
| Right stick | Move keyboard |
| Left stick up / down | Change size in 5% steps |
| Left stick left / right | Change opacity in 5% steps |
| L3 | Reset size, position, and opacity |
| L3 + R3 while PadBoard is open | Exit VitaPad |

Circle applies its effect while held. Tap it to arm the next emitted key, or double-tap within 300 ms to lock. Tap again to unlock. An unused one-shot stays armed until used. After one second, activate the same modifier again to cancel it. The visible Ctrl, Alt, and Shift keys follow the same rules. Circle on QWERTY shares Shift's state. One dot marks an armed modifier and two mark a lock.

Output keys consume one-shots. Moving the highlight, switching layouts, and arming another modifier preserve them. Symbols includes **All** to the right of **Paste** for selecting the focused field's text. **PrtSc** immediately to the right of **Ins** sends Print Screen once per press.

The floating panel absorbs clicks. Tap it to reveal its circular **Hide** control. Typing uses the controller. Move the panel with the right stick to any available work-area edge. The panel keeps keyboard focus in the receiving application.

## Exit and settings

**L1 + R1 + R3** opens the exit confirmation. Fresh Cross confirms and Circle cancels. The tray menu also provides direct exit and pause/resume.

Settings save automatically beside the executable in `settings.json`. Keep the folder writable. Export/import is available in the studio. To keep profiles during an upgrade, exit VitaPad, copy your existing `settings.json` into the new app folder, and launch the new version. Release held controls after reconnecting or resuming.

## Build

Install the .NET 10 SDK on Windows and run the following from the repository root.

```powershell
.\source\build.ps1 -Destination .\app
```

The script publishes a self-contained x64 executable and runs its engine checks. The source ZIP contains the same source tree as this repository.

For development builds and presentation replay, use these commands.

```powershell
dotnet build .\source\VitaPad.csproj -c Release
.\app\VitaPad.exe --self-test .\engine-tests.txt
.\app\VitaPad.exe --visual-test .\visual-results
```

## Validation and limits

The packaged v1.5.1 executable passed 133 engine checks and 92 visual/integration replay checks. An isolated cross-process test verified reopening the studio by launching VitaPad again, including requests during startup.

Physical Retroid/Moonlight testing remains pending. The broader live desktop typing test was interrupted by a foreground focus change, so its full suite is unverified for this release. Ctrl+Backspace and other shortcuts depend on the receiving application. Original gamepad input is also delivered to applications. Windows output injection follows application elevation restrictions.

The portable package includes the .NET runtime's license notices in `licenses`.

[PadBoard for Android](https://github.com/page-muncher/PadBoard-Android) provides the related standalone Android system keyboard.

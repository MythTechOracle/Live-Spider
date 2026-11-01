# Halloweb

*A little Halloween spirit for your desktop.*

![Halloweb's spider and silvery web](assets/halloweb-preview.png)

Halloweb is a tiny Windows desktop spider with somewhere to be. Move your pointer close and it scurries away. Leave your computer quiet and it wanders, chooses a corner, and grows a silvery web.

The spider and its silk float over ordinary desktop windows. Their transparent overlays let your clicks reach the apps underneath.

When it crosses an accessible button, a soft amber **hover glow** outlines the control beneath its body. This is Halloweb's own visual effect: it does not move your pointer or send hover, click, or activation events to the app. Controls that do not expose suitable accessibility geometry simply receive no glow.

## Meet your spider

- After **2 minutes idle**, it starts wandering.
- After **5 minutes idle**, it looks for a quiet corner and builds a web.
- Disturb it and it flees, keeping its unfinished web.
- If the pointer blocks its route home, it waits safely. Clear the route and it can return; weaving resumes when the idle and safety conditions permit.

The Windows app currently uses safe waiting for blocked returns. Automatic detours belong to the separate JavaScript behavior experiments.

## Run

Halloweb targets **Windows 10 or 11, x64** and uses the .NET Framework included with Windows. The desktop app needs no installer or Node.js runtime.

Download the Windows ZIP from [Releases](https://github.com/MythTechOracle/Live-Spider/releases), extract it, and double-click **Halloweb.exe**.

The hover glow is part of the **0.2.0 source build**. The published v0.1.0 ZIP does not include it yet.

Right-click the spider icon in the system tray; it may be inside the tray's up-arrow menu.

| Control | What it does |
| --- | --- |
| **Build a web now** | Opens a 90-second construction window while keeping pointer safety checks active. |
| **Pause / Resume** | Stops or resumes the spider and its construction. Double-clicking the tray icon also toggles pause. |
| **Bring spider near pointer** | Recalls it to your current display and clears its previous web. |
| **Show only on desktop** | Hides the overlays while another app has focus. |
| **Spider hover glow** | Toggles the visual button outline. Enabled at launch; pausing also hides it. |
| **Spider size** | Chooses Small, Original, or Large. |
| **Clear web and route memory** | Gives the spider a fresh start. |
| **Exit spider** | Closes the app. **Ctrl+Alt+Q** also quits when that shortcut is available. |

Run the executable again whenever you want the spider back. It stays on its starting display unless recalled. Exclusive full-screen apps and the Windows secure desktop may cover the overlays.

## Build from source

In PowerShell, from the repository root:

```powershell
./scripts/build.ps1
```

The build produces **dist/Halloweb.exe** using the Windows .NET Framework compiler.

To run the checks:

```powershell
./scripts/verify.ps1
```

Verification also needs Node.js for the independent JavaScript behavior tests. Native Windows source lives in `src/native/`; the JavaScript reference engine is in `src/behavior/`, with its tests in `tests/behavior/`.

## Local by design

Halloweb reads the pointer position locally so the spider can react. It uses Windows' idle-time information without recording keystrokes. Its activity map, web progress, route memory, and recent behavior history stay in memory for the current session. An explicit diagnostic command can export a local history snapshot.

Hover glow uses read-only Windows accessibility queries for control roles, bounds, availability, and process identity. It does not read control names or text, invoke controls, or save a history of the apps beneath the spider.

See [Behavior and verification](docs/BEHAVIOR.md) for the movement rules, diagnostic commands, test evidence, and the differences between the Windows app and the JavaScript experiments.

## Credits and license

Halloweb grew from human creative direction and iterative collaboration: Codex implemented the desktop app and verification work; Gemini contributed behavior prototypes and regression material.

Released under the [MIT License](LICENSE). Copyright © 2026 Nelson Williams.

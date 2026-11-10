# L-Switcher

A Windows 11+ x64 tray utility for explicit keyboard-layout shortcuts and selected-text conversion. Prototype written in C# / Windows Forms for .NET Framework 4.8. No third-party libraries or separate runtime installation.

## Build and run

Install Visual Studio Build Tools 2022 with the components in `.vsconfig` (already installed on the development machine). A modern .NET SDK is not required.

```powershell
.\build.ps1 -Test
.\bin\Release\L-Switcher.exe
```

The release executable is standalone; copy `L-Switcher.exe` to a permanent folder before enabling startup. GitHub Actions builds it and uploads the `L-Switcher-win-x64` artifact on pushes and pull requests.

## Configure

- First launch opens settings with no assignments. Add rows under **Layouts**, select an installed layout, focus a shortcut field, and press a key or combination. **Clear** removes its assignment.
- **Layouts** also provides independent tray-label colors for every installed layout, with a live preview and **Default** reset. Colors do not require key bindings; unavailable layouts retain their saved colors.
- Standalone left/right modifiers act on release after a short tap (300 ms by default). Keyboard combinations, mouse actions, and long holds cancel the tap. Assigned media and ordinary keys suppress their original actions. Shortcuts involving Windows-reserved combinations may be intercepted by Windows.
- **Conversion** selects one layout pair, a shortcut, and a direction. `Swap both ways` detects each word's source from characters unique to either layout. Ambiguous punctuation uses the preceding word's direction. An ambiguous word inherits that direction; initially the first layout is used. One-way modes explicitly select the source.
- The same tab configures selection shortcuts for **UPPERCASE**, **lowercase**, and **Invert case**. They use invariant Unicode casing and work without a configured layout pair.
- **Exclusions** disables every shortcut while an executable name or full executable path matches the foreground application.
- **General** controls Windows startup, the tap limit, and an optional **Pause / Resume** shortcut. The pause shortcut stays active during manual pause; excluded applications disable it too. Startup and every shortcut are unassigned/disabled initially.
- The **General** shortcut test field displays the detected key/combination, tap or hold duration, and the action assigned in the current settings draft. It never executes the action.
- Settings and the exclusion editor support per-monitor DPI changes. Buttons and rows size to their contents; large windows fit the monitor work area and tab contents can scroll. DPI handling is embedded in the executable; no extra `.exe.config` is required.
- **Apply** saves; **OK** saves and closes; **Cancel** or the close button discards unapplied changes. The application remains in the tray.
- Click the tray label for settings; right-click for pause/resume or exit. Pause resets on restart. Launching the executable again opens the existing instance's settings.

Settings are stored in `%LOCALAPPDATA%\L-Switcher\settings.json`, with atomic replacement and a `.bak` backup of the previous valid configuration. Corrupt configuration triggers recovery with a warning. Removed layouts retain their bindings and appear as `Layout unavailable`; unavailable actions are disabled.

## Selection conversion

Characters map by physical scan-code positions, including shifted and AltGr characters where both layouts produce an unambiguous single character. Case, whitespace, line breaks, and characters outside the pair are retained. Dead-key compositions and IME input are not mapped. The active layout stays unchanged.

Microsoft Word uses its native object model to edit selected characters while preserving their formatting and selection. Protected/read-only documents and tracked revisions are skipped. Other applications use UI Automation to verify an editable, nonempty selection, then copy/paste with clipboard restoration. RTF preserves supported character and paragraph formatting where possible; plain text is the fallback. Controls that do not expose an editable selection are skipped. If another application changes the clipboard during conversion, its newer contents are retained. Clipboard formats that cannot be backed up stop conversion.

Word formatting acceptance cases: mixed English/Russian text, bold, italic, color, multiple paragraphs, retained selection, unchanged active layout, empty selection, and read-only selections. VS Code and Chrome remain manual compatibility targets. Elevated applications, Remote Desktop, and games are deferred. Complex Word content (fields, tables, embedded objects) and advanced RTF features need additional compatibility testing.

## Validation

```powershell
.\build.ps1 -Test             # conversion, settings recovery/conflicts, modifier state, RTF, actual installed layouts
.\build.ps1 -UISmoke          # additionally opens/closes settings and renders all four tabs
.\build.ps1 -WordSmoke        # additionally creates a separate Word document and closes it without saving
.\build.ps1 -ClipboardSmoke   # additionally verifies UIA/RTF conversion, selection, read-only and clipboard restoration in a temporary text control
.\build.ps1 -DpiSmoke         # verifies 100–300% layouts, live DPI messages, added rows and the exclusion dialog; renders PNG previews
```

The Word smoke test requires Microsoft Word and desktop/COM access. It uses its own new Word instance and does not modify existing documents. UI smoke output is placed in `tests/bin/Release/settings-*.png`.

Verified on the development machine on 2026-11-10: Release x64 build and 99 assertions in the combined `--word-smoke --clipboard-smoke --ui-smoke` run. Checks cover all four settings tabs and tray lifecycle, native Word formatting/selection, clipboard/UIA conversion, case conversion, pause/resume semantics, shortcut capture, per-layout colors, exclusion matching, settings migration, recovery, and assignment conflicts. All tabs were visually inspected. GitHub Actions is configured; its hosted run has not been executed from this local implementation session. Manual VS Code and Chrome acceptance remains pending.

DPI follow-up on 2026-11-10: Release rebuild and 836 assertions passed with `--ui-smoke --dpi-smoke`. Rendered and inspected layouts at 100–300%, tested repeated `WM_DPICHANGED` transitions, dynamically added bindings, and the actual exclusion dialog. The process manifest was verified as PerMonitorV2. These transitions are simulated with window messages; moving between physical monitors with different scale factors has not been separately exercised. To load a rebuilt executable, exit the running instance through the tray before launching it again.

See [PLAN.md](PLAN.md) for accepted decisions and remaining work. Licensing and releases have not yet been decided.

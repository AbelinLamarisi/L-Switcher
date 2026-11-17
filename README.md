# L-Switcher

A Windows 11+ x64 tray utility for explicit keyboard-layout shortcuts and selected-text conversion. Prototype written in C# / Windows Forms for .NET Framework 4.8. No third-party libraries or separate runtime installation.

## Build and run

Install Visual Studio Build Tools 2022 with the components in `.vsconfig` (already installed on the development machine). A modern .NET SDK is not required.

```powershell
.\build.ps1 -Test
.\bin\Release\L-Switcher.exe
```

The release executable is standalone; copy `L-Switcher.exe` to a permanent folder before enabling startup.

Build versions are embedded in the executable: local builds always use the full Git commit hash and never query GitHub. In GitHub Actions, a published-release build uses the exact release tag; other events use the commit hash. This metadata also appears as the executable's Product version. Identifying a running build requires no runtime network or Git access.

## GitHub Actions

The [Windows build and tests workflow](https://github.com/AbelinLamarisi/L-Switcher/actions/workflows/build.yml) runs on pushes to `main`, `v*` tags, pull requests, and manual dispatch. Documentation-only changes (Markdown, `docs/`, license files, or `.gitignore`) skip automatic branch/PR runs. Feature-branch pushes are checked through their PR, avoiding duplicate push/PR builds. Version tags always run.

One Windows Server 2022 job builds Release x64 and the test executable with MSBuild, then runs all core and UI/DPI checks once. A failed build or assertion fails the job. The job has a 6-minute timeout; tests have a 2-minute timeout. Superseded runs for the same branch/PR are canceled. Tests require Windows APIs, so they remain together on Windows without extra jobs or a platform matrix.

Download artifacts from a workflow run's summary:

- `L-Switcher-win-x64`: the tested standalone `L-Switcher.exe`, retained for 7 days and uploaded after all checks pass on main/tag/manual runs. PR runs validate the build without retaining another executable.
- `L-Switcher-test-results`: build/test output and UI/DPI PNG previews, retained for 3 days and uploaded even when checks fail.

Artifacts require access to this private repository. Word and clipboard integration checks remain local because they require Word or foreground desktop/clipboard access. This workflow does not create GitHub Releases or an installer.

## Configure

- First launch opens settings with no assignments. **Layouts → Add layout** adds a row, scrolls it into view, and focuses its layout selector.
- Shortcut fields stay idle when reached with Tab or Shift+Tab. Click a field or press Enter to start recording (`Press a shortcut…`); press a key/combination, or Esc to cancel and restore its prior value. **Clear** is available only for assigned shortcuts. Conflicting saves identify the shortcut and all conflicting actions, then select and focus the most recently edited field.
- **Layouts** includes **Default Color** for tray text and background. In each layout row, open the colored preview to customize either color or choose **Use default colors**. Overrides follow the layout, even if its shortcut changes.
- Standalone left/right modifiers act on release after a short tap (300 ms by default). Keyboard combinations, mouse actions, and long holds cancel the tap. Assigned media and ordinary keys suppress their original actions. Shortcuts involving Windows-reserved combinations may be intercepted by Windows.
- **Conversion** selects one layout pair, a shortcut, and a direction. One-way direction labels use the selected layout names and update with either selector. The word-detection explanation appears only for `Swap both ways`, which detects each word's source from characters unique to either layout. Ambiguous punctuation uses the preceding word's direction. An ambiguous word inherits that direction; initially the first layout is used.
- The same tab configures selection shortcuts for **UPPERCASE**, **lowercase**, and **Invert case**. They use invariant Unicode casing and work without a configured layout pair.
- In **Keyboard layout conversion**, **Deselect text after conversion** places the caret after the converted text instead of keeping it selected. It is disabled by default and does not affect case conversion.
- **Exclusions** disables every shortcut while an executable name or full executable path matches the foreground application.
- **General** controls Windows startup, the tap limit, and an optional **Pause / Resume** shortcut. The pause shortcut stays active during manual pause; excluded applications disable it too. Startup and every shortcut are unassigned/disabled initially.
- The **General** shortcut test field uses the same click/Enter recording and Esc cancellation. It displays the detected key/combination, tap or hold duration, and the action assigned in the current settings draft without executing it. Clearing the tester resets its action/timing feedback and never dirties the settings draft.
- Settings and the exclusion editor support per-monitor DPI changes. Buttons and rows size to their contents; large windows fit the monitor work area and tab contents can scroll. DPI handling is embedded in the executable; no extra `.exe.config` is required.
- **Apply** is enabled only when settings differ from the last successfully applied draft; reverting an edit or saving disables it again. **OK** saves and closes, including unchanged defaults on first launch. **Cancel** or the close button discards unapplied changes. Exclusion **Remove** requires a selected entry. The fixed-size settings window retains per-monitor DPI scaling.
- The tray uses orange pause bars for manual pause and a separate red cross badge for an excluded application. Both marks and both tooltip explanations appear when the two states coincide.
- Click the tray label for settings; right-click for pause/resume or exit. Pause resets on restart. Launching the same build opens settings in the existing process and keeps its current state. A different build gracefully replaces the running instance after conversion/clipboard restoration and cleanup; versions are compared for equality, not ordered. Saved settings are retained; replacement discards unapplied edits. If shutdown fails within 10 seconds, the new launch reports an error without starting a second instance or terminating the running process. Builds without the new identifier protocol are replaced once.

Settings are stored in `%LOCALAPPDATA%\L-Switcher\settings.json`, with atomic replacement and a `.bak` backup of the previous valid configuration. Corrupt configuration triggers recovery with a warning. Removed layouts retain their bindings and appear as `Layout unavailable`; unavailable actions are disabled.

## Selection conversion

Characters map by physical scan-code positions, including shifted and AltGr characters where both layouts produce an unambiguous single character. Case, whitespace, line breaks, and characters outside the pair are retained. Dead-key compositions and IME input are not mapped. The active layout stays unchanged.

Microsoft Word uses its native object model to edit selected characters while preserving their formatting and selection. Protected/read-only documents and tracked revisions are skipped. Other applications use UI Automation to verify an editable, nonempty selection, then copy/paste with clipboard restoration. RTF preserves supported character and paragraph formatting where possible; plain text is the fallback. Controls that do not expose an editable selection are skipped. If another application changes the clipboard during conversion, its newer contents are retained. Clipboard formats that cannot be backed up stop conversion.

Word formatting acceptance cases: mixed English/Russian text, bold, italic, color, multiple paragraphs, retained selection, unchanged active layout, empty selection, and read-only selections. VS Code and Chrome remain manual compatibility targets. Elevated applications, Remote Desktop, and games are deferred. Complex Word content (fields, tables, embedded objects) and advanced RTF features need additional compatibility testing.

## Validation

```powershell
.\build.ps1 -Test             # cross-process instance replacement, conversion, settings, modifier state, RTF, installed layouts
.\build.ps1 -UISmoke          # additionally opens/closes settings and renders all four tabs
.\build.ps1 -WordSmoke        # additionally creates a separate Word document and closes it without saving
.\build.ps1 -ClipboardSmoke   # additionally verifies UIA/RTF conversion, selection, read-only and clipboard restoration in a temporary text control
.\build.ps1 -DpiSmoke         # verifies 100–300% layouts, live DPI messages, added rows and the exclusion dialog; renders PNG previews
```

The Word smoke test requires Microsoft Word and desktop/COM access. It uses its own new Word instance and does not modify existing documents. UI smoke output is placed in `tests/bin/Release/settings-*.png`.

Verified on the development machine on 2026-11-10: Release x64 build and 99 assertions in the combined `--word-smoke --clipboard-smoke --ui-smoke` run. Checks cover all four settings tabs and tray lifecycle, native Word formatting/selection, clipboard/UIA conversion, case conversion, pause/resume semantics, shortcut capture, per-layout colors, exclusion matching, settings migration, recovery, and assignment conflicts. All tabs were visually inspected. Hosted GitHub Actions run 42817694355 subsequently passed the Release x64 build and 844 core/UI/DPI assertions, with executable and diagnostic artifacts. Manual VS Code and Chrome acceptance remains pending.

DPI follow-up on 2026-11-10: Release rebuild and 836 assertions passed with `--ui-smoke --dpi-smoke`. Rendered and inspected layouts at 100–300%, tested repeated `WM_DPICHANGED` transitions, dynamically added bindings, and the actual exclusion dialog. The process manifest was verified as PerMonitorV2. These transitions are simulated with window messages; moving between physical monitors with different scale factors has not been separately exercised. Launching a rebuilt executable now replaces the running instance automatically. Pre-shutdown-protocol builds are asked to exit their UI loop gracefully when identifiable; if they cannot be reached, use Exit in the tray once.

See [PLAN.md](PLAN.md) for accepted decisions and remaining work. MIT is selected for the future open-source release; the license file and copyright-holder details will be handled before distribution. Update checking remains disabled until a public metadata source is available; test builds are updated manually.

Instance replacement follow-up (2026-11-13): Release build and 870 core/UI/DPI assertions passed. Isolated helper processes verify graceful cleanup before takeover, serialization of simultaneous launches, timeout with the old instance left alive, legacy UI-loop shutdown, and replacement of an instance with open settings. `--ui-smoke` includes the tray handover integration check. Test identities are unique and do not signal the user's running instance.

Settings/tray follow-up (2026-11-13): all six approved workflow improvements are implemented and 967 core/UI/DPI assertions passed. The UI suite also checks keyboard-only capture/navigation, Esc cancellation, conflict details/focus in both edit orders, draft/button state after changes and reverts, tester reset, direction names, and scrolling new rows into view. All four pause/exclusion icon and tooltip combinations are checked. UI and tray PNG previews were inspected locally.

Color compatibility fix (2026-11-17): foreground-only and background-only layout entries are supported independently, with inherited missing components. Configuration validation errors now recover from the valid backup or defaults with a warning instead of aborting startup. Release build and 1140 core/UI/DPI assertions passed; the existing user configuration was also loaded read-only without warnings or file changes.

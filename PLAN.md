# L-Switcher — Plan and Decisions

## Working agreement

Implementation authorized by the user on 2026-11-10. Follow the accepted decisions below.
Maintain this plan in English. Proposals are not accepted decisions until agreed.
Update this plan whenever the user makes a decision; promptly record accepted decisions and revise superseded proposals or open questions. This is the user's standing preference for all projects that use a plan.

## Accepted decisions

- Primarily for personal use, with public open-source distribution planned.
- Repository name: L-Switcher.
- Keep the GitHub repository private for now. Future open-source distribution remains planned.
- Target Windows 11 and later; ARM support is out of scope.
- Minimal UI: system tray icon(s) and a settings window.
- Support different display DPI/scale factors, including live transitions between monitors; text and buttons must not be clipped.
- Support startup with Windows, controlled by a checkbox in settings; disabled by default.
- Settings UI is English. The tray icon displays a two-letter abbreviation of the active layout.
- Allow users to choose a separate tray-label color for each keyboard layout in settings.
- No automatic language detection or automatic switching while typing.
- Allow direct activation of any installed keyboard layout through assigned keys, rather than cycling through layouts.
- Support standalone modifier keys, distinguish left/right keys, and support media keys as assignment targets. Example mappings (not fixed defaults): Left Ctrl → Russian, Left Shift → English, a media key → German.
- Standalone Ctrl/Shift actions trigger on release after a short tap only, provided the key did not participate in a keyboard shortcut or mouse action. Long holds do not trigger the action.
- Assigned media keys suppress their original function.
- Support ordinary key combinations such as Ctrl+Alt+R.
- First launch has no key assignments; the user configures all bindings.
- Include a tray command to pause hotkeys.
- Include a user-assignable Pause/Resume hotkey in the prototype. It remains active during manual pause so it can resume processing, but is disabled along with all other L-Switcher hotkeys in excluded applications.
- Hotkeys remain paused until manually resumed or the application restarts. Restarting resumes hotkeys.
- Short-tap time limit defaults to 300 ms and is configurable in settings.
- Store settings as JSON in `%LOCALAPPDATA%\L-Switcher\settings.json`, with a schema version, safe replacement on save, and a backup of the previous valid configuration.
- Aim for compatibility with most Windows applications. Games are out of scope for now.
- Initial acceptance applications: VS Code, Chrome, and Word. Elevated applications and Remote Desktop support are deferred.
- Per-application exclusions are required in the prototype. In an excluded foreground application, disable all L-Switcher hotkeys and let keys retain their normal behavior.
- Each exclusion can match by executable name or full executable path, selected in the UI.
- Exclusions apply only while the matching application's window is active. When focus moves to a non-excluded application, hotkeys resume unless manually paused.
- If a configured layout is removed from Windows, retain its binding, show `Layout unavailable`, and disable that action while the layout is unavailable.
- Run a single application instance. Every new launch replaces the running instance: request graceful shutdown, wait for cleanup and release of the single-instance lock, then start the newly launched executable. Do not run both instances concurrently.
- User simplified replacement on 2026-11-13: do not compare versions or timestamps for now; even an equal or older executable replaces the running instance. This supersedes the earlier version-comparison/no-downgrade rule.
- If the running instance cannot exit, report the failure instead of starting a second instance or forcibly terminating it. Saved settings survive replacement; unapplied settings edits are discarded as on closing settings.
- Use one global configured conversion pair. Direction is selected in settings: `Swap both ways`, `First → Second`, or `Second → First`, independently of the currently active layout. Bidirectional mode swaps mixed text within the pair.
- Leave the active layout unchanged after conversion.
- Preserve case, spaces, and line breaks. Convert punctuation according to physical key positions. Leave characters outside the configured pair unchanged.
- Temporary clipboard use with restoration of its contents is allowed if that implementation approach is selected.
- Preserve internal text formatting where possible, including in the prototype. If formatting cannot be preserved, still convert using plain text with formatting loss. This supersedes the earlier prohibition on plain-text fallback.
- Keep converted text selected by default. The `Keyboard layout conversion` group includes `Deselect text after conversion`, disabled by default; when enabled, collapse the selection to the end after layout conversion. This option does not affect selection case commands. Accepted on 2026-11-14.
- With no selection, the conversion command does nothing.
- Do nothing for read-only selections.
- Include selection case conversion in the prototype: uppercase, lowercase, and invert case, each with a user-assignable hotkey. Approved during the interactive feature review.
- Distribution must not require users to install dependencies: a standalone executable.
- Accepted on 2026-11-14: successful CI builds must provide a downloadable build artifact containing the standalone executable. Routine CI artifacts and GitHub Releases are separate: publish a binary to GitHub Releases only for an explicitly requested/created release, never automatically for ordinary pushes or CI success.
- Installing the development toolchain on this machine should be straightforward.
- Technology stack: C# + Windows Forms targeting .NET Framework 4.8, with Windows API interop where needed.
- Do not bundle the runtime. Use the Windows-provided .NET Framework on Windows 11 and later; no separate modern .NET Desktop Runtime prerequisite.
- Plan new-version checking, but defer enabling it until a public update metadata source is available. Until then, update test builds manually. Once enabled, notify the user when an update is available; downloading and replacing the executable remain manual. Do not add private-source authentication or embed GitHub credentials in the executable. No public update source has been authorized for creation yet.
- In bidirectional conversion, convert mixed selections word by word. Resolve ambiguous punctuation from the preceding word's source layout (user clarification on 2026-11-10).
- Word formatting acceptance starts with bold, italic, color, and multiple paragraphs (accepted on 2026-11-10).

## Proposals awaiting agreement

- First release focuses on layout activation, selection conversion, and settings.

## Accepted settings and tray UI

- Application icon selected on 2026-11-14: tilted violet/coral S-shaped exchange ribbon with a large white L, without outlines (variant 1 of the final comparison). Embed a multi-resolution ICO in the EXE and application windows; retain the active-layout/status indicator in the tray. Implemented with transparent PNG exports and ten ICO sizes from 16 to 256 px.

- Use tabs: `Layouts`, `Conversion`, `Exclusions`, and `General`.
- The `Exclusions` tab contains a list with `Add` and `Remove` buttons and a choice of matching by executable name or full executable path.
- Users add layout-binding rows with an `Add` button; do not prepopulate bindings.
- Start shortcut recording with a click or Enter. Provide a `Clear` button.
- Conflicting assignments block saving and show an explanation.
- Include a shortcut test field in the prototype settings. It shows the detected key or combination, short tap versus hold, and assigned action without executing that action while testing.
- Use `OK`, `Cancel`, and `Apply` buttons.
- Closing settings with the window close button discards unapplied changes and leaves the application running in the tray.
- Automatically open settings on first launch. Subsequent launches start in the tray.
- A single left click on the tray icon opens settings. Right click opens `Settings…`, `Pause hotkeys` / `Resume hotkeys`, and `Exit`.
- While paused, retain the two-letter active-layout label and add a small pause mark.
- Give application exclusions a distinct tray mark and show `Hotkeys disabled for this app` in the tooltip. Include both statuses when manual pause and an exclusion apply together.
- The tray tooltip shows the full layout name and pause status, e.g. `English (United States) — Hotkeys paused`.
- Layouts of the same language share their two-letter label (e.g. `EN`); the tooltip distinguishes their full names.
- The `Conversion` tab contains two layout selectors, a shortcut field, and the `Direction` selector described above.
- Use standard Windows system styling for the prototype; no custom theme requirement.

## Feature ideas for discussion (not approved)

Inspired by Punto Switcher feature descriptions and the current L-Switcher scope:

- Deferred by the user: explicit clipboard-text conversion, separate from selection conversion; this command intentionally changes clipboard contents. Not part of the prototype.
- Deferred by the user: transliteration as a separate command from keyboard-layout conversion. Not part of the prototype.
- Deferred by the user: optional brief on-screen layout indicator after switching with an L-Switcher hotkey. Not part of the prototype.
- Deferred by the user: settings import/export through a JSON file. Not part of the prototype.
- Deferred by the user: manually invoked text snippets. Not part of the prototype.
- Deferred by the user: a separate convert-previous-word command without prior selection. Not part of the prototype; the existing selection command must still do nothing without a selection.
- Do not add these features until approved. Automatic switching remains out of scope.
- Research: [official Punto Switcher page](https://yandex.ru/soft/punto/win/); [third-party hotkey overview](https://puntos.ru/news/4-punto-switcher-gorjachie-klavishi-po-umolchaniju.html). The official detailed help was inaccessible during this review; third-party descriptions are not verification of the latest installed version.

## Technology decision rationale

- C# and WinForms simplify the small settings UI and tray integration.
- .NET Framework 4.8 satisfies the requirement to use the runtime provided with Windows, without bundling a modern .NET runtime or requiring its installation.
- Accept the older platform and its library ecosystem for this Windows-only utility.
- C++/Win32, Rust/Win32, and self-contained modern .NET were considered but not selected.
- Development tools installed on 2026-11-10: Visual Studio Build Tools 2022 17.14.44, including MSBuild, the Roslyn C# compiler, NuGet build tasks, and .NET Framework 4.8 SDK and targeting/reference assemblies. The component selection is recorded in `.vsconfig`. A modern .NET SDK is not required for the selected .NET Framework build approach.
- Verification passed: MSBuild restored and rebuilt a temporary C# project targeting .NET Framework 4.8 and x64, and its executable successfully created WinForms `Form` and `NotifyIcon` objects. No restart was required. This was an environment check outside the repository, not application implementation.
- Do not promise exact memory or binary size without a prototype.
- Sources: [.NET Framework on Windows](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-10), [WinForms tray component](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-windows-forms).

## Accepted development tooling decisions

- Use minimal Visual Studio Build Tools rather than installing the full Visual Studio IDE. Select the components needed to build C# WinForms targeting .NET Framework 4.8 when implementation begins.
- Third-party open-source libraries are allowed, but ask the user before adding each library. Prefer popular, actively maintained libraries; this preference does not replace individual approval.
- Set up GitHub Actions automated builds from the start of implementation, rather than deferring CI until later.
- Optimize GitHub Actions usage for the user's GitHub Free plan: one Windows job with all core/UI/DPI checks, automatic runs for relevant main/PR changes and version tags, manual dispatch, cancellation of superseded runs, short timeouts, and short artifact retention. Skip documentation-only branch/PR changes and duplicate feature-branch push runs; retain tested executables for main/tag/manual builds rather than every PR.
- Tool installation was explicitly authorized and completed on 2026-11-10. Application implementation was subsequently authorized on the same day.

## Open questions

### Selection conversion

- User deferred the final shared-alphabet ambiguity rule until practical testing. For now, words whose letters occur in both layouts inherit the preceding direction, initially First → Second; this remains a provisional implementation default, not the final accepted rule.
- User chose basic Word formatting acceptance for the prototype: bold, italic, color, and multiple paragraphs. Broader checks for lists, tables, hyperlinks, mixed fonts, and sizes are deferred until later. Plain-text fallback remains allowed.

### UI and distribution

- Future update-check UI is agreed: `Check for updates` plus `Automatically check for updates`, disabled by default. When enabled, check on startup if due and subsequently no more than once per day. Background checks notify only when a new version is available; no-update results and temporary source failures remain silent. Manual checks always report their result. Installation remains manual. Activation still waits for a public metadata source; choose hosting then.
- MIT selected for the future open-source release. The repository remains private. Add the license file and confirm copyright-holder details separately before distribution.
- No hard numerical resource or executable-size limits for now. First measure executable size, memory, and CPU usage while idle in the tray, with settings open, and after repeated conversions. Avoid perceptible input delays, minimize idle CPU use, and investigate sustained memory growth. Decide optimization targets from measurements.

## Next steps

### Approved settings and tray improvements (2026-11-13)

Keep the settings window fixed in size and retain per-monitor DPI scaling. The first two items were implemented first; all six improvements below are now implemented.

- [x] **Shortcut recording.** Tab and Shift+Tab move between idle fields. A click or Enter starts recording and displays `Press a shortcut…`. Esc cancels recording and restores the previous value. Keyboard-only navigation and recording are verified in both assignment fields and the shortcut tester.
- [x] **Conflicting assignments.** When saving detects a duplicate, report the shortcut and both assigned actions. After the message closes, select the tab containing the most recently edited conflicting field and focus it. Verified with a layout activation and a conversion command assigned the same shortcut, in both edit orders, including re-recording an unchanged value.
- [x] **Adding a layout.** A compact `Add layout` button is below the table on the left. Adding a row scrolls it into view and focuses its layout selector, including when the table extends beyond the visible area.
- [x] **Conversion direction.** Both one-way direction options use the selected layout names and refresh when either selector changes. The word-detection explanation is shown only for `Swap both ways`.
- [x] **Button states and tester feedback.** `Apply` is enabled only while the draft differs from the last successfully applied settings; reverting an edit or saving disables it. Exclusion `Remove` requires a selection and shortcut `Clear` requires an assigned value. Clearing the tester resets its action and timing feedback. OK still saves unchanged defaults to complete first-launch setup.
- [x] **Tray status.** Orange pause bars represent manual pause; a separate red cross badge represents an excluded application. All four icon/tooltip combinations are verified, including both explanations when pause and exclusion coincide.

Validation on 2026-11-13: Release x64 build and 967 core/UI/DPI assertions passed. UI checks cover keyboard-only capture in assignment/test fields, Esc/focus cancellation, conflict messages and focus in both edit orders, overflowing layout rows, changes/reverts/successful and failed saves, tester reset, direction labels, and tray states. The settings and tray previews were visually inspected.

### Prototype implementation (2026-11-10)

- On 2026-11-14, added the persisted `Deselect text after conversion` option in the keyboard-layout conversion group, disabled by default and independent of case commands. Word and clipboard/UIA paths collapse to the converted text end, including unchanged editable text; empty/read-only selections remain untouched. Release build, 973 core/UI/DPI assertions and the 163-assertion local Word/clipboard suite passed.
- Implemented the .NET Framework 4.8 x64 project, settings UI, tray, single-instance activation, keyboard/mouse hooks, direct layout activation, exclusions, startup control, and JSON recovery/backup.
- On 2026-11-13, replaced open-existing behavior with unconditional graceful handover on every new launch. Startup is serialized; hooks/tray/windows and in-flight conversions finish before the instance lock is released. A 10-second timeout reports failure without starting a second instance. Legacy UI-loop shutdown is supported for identifiable pre-protocol builds. Cross-process tests cover cleanup ordering, concurrent launches, timeout without forced termination, legacy handover, and tray/settings shutdown. Release build and 870 core/UI/DPI assertions passed locally.
- Implemented physical-key conversion, word-context punctuation, native Word character edits, and a guarded clipboard/UI Automation path with RTF/plain-text support.
- Added dependency-free automated tests, a build script, UI/Word smoke tests, and GitHub Actions build/artifact configuration.
- Implemented the subsequently approved Pause/Resume shortcut (including resuming during manual pause and exclusion passthrough), selection uppercase/lowercase/invert-case shortcuts, and a non-executing shortcut test field with tap/hold timing and draft assignment feedback. Existing schema-one settings default these new shortcuts to unassigned.
- Implemented independent per-layout tray-label colors, with a color picker, preview, default reset, persistence, and retention for unavailable layouts.
- Implemented per-monitor DPI handling in the executable manifest and a shared DPI-aware form base, with stable 96-DPI layout metrics, font/spacing rescaling, content-sized buttons/rows, dynamic binding scaling, and monitor work-area limits. No sidecar configuration is required. DPI/UI follow-up passed 836 assertions across 100–300% and repeated DPI messages, including the exclusion dialog; physical mixed-DPI monitor movement remains a manual check.
- Release x64 build and the full local combined test run passed with 99 assertions on 2026-11-10. Hosted Windows build and core/UI/DPI checks subsequently passed 844 assertions in run 42817694355, producing executable and diagnostic artifacts. VS Code and Chrome manual acceptance remains pending.
- Verified native Word conversion with bold/italic/color, paragraphs, selection retention, unchanged active layout, and protected/empty selections. Verified the clipboard/UIA path on a temporary editable RichTextBox, including RTF formatting, selection retention, read-only behavior, and restoration of text/custom clipboard formats. These checks do not replace application-specific acceptance in VS Code and Chrome.
- Verify the prototype in VS Code, Chrome, and Word; distinguish automated checks from manual compatibility acceptance in the completion report.
- Review shared-alphabet defaults and complex rich-text behavior after use.
- Update checking activation is deferred until a public metadata source exists; its future check timing and UI are agreed. Licensing and advanced compatibility decisions can wait.

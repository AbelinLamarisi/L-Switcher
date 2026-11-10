# L-Switcher — Plan and Decisions

## Working agreement

Planning and brainstorming only. Do not implement until explicitly requested.
Maintain this plan in English. Proposals are not accepted decisions until agreed.

## Accepted decisions

- Primarily for personal use, with public open-source distribution planned.
- Repository name: L-Switcher.
- Keep the GitHub repository private for now. Future open-source distribution remains planned.
- Target Windows 11 and later; ARM support is out of scope.
- Minimal UI: system tray icon(s) and a settings window.
- Support startup with Windows, controlled by a checkbox in settings; disabled by default.
- Settings UI is English. The tray icon displays a two-letter abbreviation of the active layout.
- No automatic language detection or automatic switching while typing.
- Allow direct activation of any installed keyboard layout through assigned keys, rather than cycling through layouts.
- Support standalone modifier keys, distinguish left/right keys, and support media keys as assignment targets. Example mappings (not fixed defaults): Left Ctrl → Russian, Left Shift → English, a media key → German.
- Standalone Ctrl/Shift actions trigger on release after a short tap only, provided the key did not participate in a keyboard shortcut or mouse action. Long holds do not trigger the action.
- Assigned media keys suppress their original function.
- Support ordinary key combinations such as Ctrl+Alt+R.
- First launch has no key assignments; the user configures all bindings.
- Include a tray command to pause hotkeys.
- Hotkeys remain paused until manually resumed or the application restarts. Restarting resumes hotkeys.
- Short-tap time limit defaults to 300 ms and is configurable in settings.
- Store settings as JSON in `%LOCALAPPDATA%\L-Switcher\settings.json`, with a schema version, safe replacement on save, and a backup of the previous valid configuration.
- Aim for compatibility with most Windows applications. Games are out of scope for now.
- Use one global configured conversion pair. Direction is selected in settings: `Swap both ways`, `First → Second`, or `Second → First`, independently of the currently active layout. Bidirectional mode swaps mixed text within the pair.
- Leave the active layout unchanged after conversion.
- Temporary clipboard use with restoration of its contents is allowed if that implementation approach is selected.
- Preserve internal text formatting where possible, including in the prototype. If formatting cannot be preserved, still convert using plain text with formatting loss. This supersedes the earlier prohibition on plain-text fallback.
- Keep the converted text selected.
- With no selection, the conversion command does nothing.
- Distribution must not require users to install dependencies: a standalone executable.
- Installing the development toolchain on this machine should be straightforward.
- Technology stack: C# + Windows Forms targeting .NET Framework 4.8, with Windows API interop where needed.
- Do not bundle the runtime. Use the Windows-provided .NET Framework on Windows 11 and later; no separate modern .NET Desktop Runtime prerequisite.

## Proposals awaiting agreement

- First release focuses on layout activation, selection conversion, and settings.

## Accepted settings and tray UI

- Use tabs: `Layouts`, `Conversion`, and `General`.
- Users add layout-binding rows with an `Add` button; do not prepopulate bindings.
- Capture a shortcut by focusing its field and pressing the desired key or combination. Provide a `Clear` button.
- Conflicting assignments block saving and show an explanation.
- Use `OK`, `Cancel`, and `Apply` buttons.
- Automatically open settings on first launch. Subsequent launches start in the tray.
- A single left click on the tray icon opens settings. Right click opens `Settings…`, `Pause hotkeys` / `Resume hotkeys`, and `Exit`.
- While paused, retain the two-letter active-layout label and add a small pause mark.
- The tray tooltip shows the full layout name and pause status, e.g. `English (United States) — Hotkeys paused`.
- Layouts of the same language share their two-letter label (e.g. `EN`); the tooltip distinguishes their full names.
- The `Conversion` tab contains two layout selectors, a shortcut field, and the `Direction` selector described above.
- Use standard Windows system styling for the prototype; no custom theme requirement.

## Technology decision rationale

- C# and WinForms simplify the small settings UI and tray integration.
- .NET Framework 4.8 satisfies the requirement to use the runtime provided with Windows, without bundling a modern .NET runtime or requiring its installation.
- Accept the older platform and its library ecosystem for this Windows-only utility.
- C++/Win32, Rust/Win32, and self-contained modern .NET were considered but not selected.
- Local check: `dotnet` is available, but `dotnet --list-sdks` returned no SDKs. Choose and verify the build tools and .NET Framework 4.8 targeting/reference assemblies before implementation. No tools have been installed for this project yet.
- Do not promise exact memory or binary size without a prototype.
- Sources: [.NET Framework on Windows](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-10), [WinForms tray component](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-windows-forms).

## Accepted development tooling decisions

- Use minimal Visual Studio Build Tools rather than installing the full Visual Studio IDE. Select the components needed to build C# WinForms targeting .NET Framework 4.8 when implementation begins.
- Third-party open-source libraries are allowed, but ask the user before adding each library. Prefer popular, actively maintained libraries; this preference does not replace individual approval.
- Set up GitHub Actions automated builds from the start of implementation, rather than deferring CI until later.
- These decisions do not authorize starting implementation or installing tools yet; the planning-only agreement remains in effect.

## Open questions

### Selection conversion

- Define ambiguous shared-character behavior if `Swap both ways` is selected for overlapping alphabets. Explicit one-way modes are already agreed.
- Preserve case, line breaks, and characters outside the selected pair? How should punctuation and ambiguous mappings behave?
- Define supported rich-text formats/applications for formatting-preservation acceptance checks; plain-text fallback is allowed.
- Is conversion of read-only selections required, and where should the result go?

### Compatibility

- Which applications are the initial acceptance targets?
- Are elevated applications, terminals, and Remote Desktop required?
- Are per-application exclusions needed?

### UI and distribution

- Manual releases or update checking?
- Open-source license?
- Resource usage and executable size preferences?

## Next steps

### Minimum decisions before the prototype

- Resolve direction-setting semantics for overlapping alphabets; all key bindings are user-defined with no initial assignments.
- Agree on formatting-preservation acceptance cases.
- Name initial application acceptance targets and decide whether elevated apps and RDP can be deferred.
- Licensing, updates, and advanced compatibility decisions can wait.
- Prototype implementation still requires an explicit request; planning continues for now.

1. Resolve key and conversion semantics.
2. Agree on initial compatibility targets.
3. When implementation is authorized, install the required minimal Build Tools components and set up local builds and GitHub Actions.
4. Agree on UI, packaging, licensing, and acceptance criteria.

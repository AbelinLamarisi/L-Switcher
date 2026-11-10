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
- No automatic language detection or automatic switching while typing.
- Allow direct activation of any installed keyboard layout through assigned keys, rather than cycling through layouts.
- Support standalone modifier keys, distinguish left/right keys, and support media keys as assignment targets. Example mappings (not fixed defaults): Left Ctrl → Russian, Left Shift → English, a media key → German.
- Aim for compatibility with most Windows applications. Games are out of scope for now.
- Convert selected text between a configured pair of layouts; swap mixed text in both directions within that pair.
- Keep the converted text selected.
- With no selection, the conversion command does nothing.
- Distribution must not require users to install dependencies: a standalone executable.
- Installing the development toolchain on this machine should be straightforward.
- Technology stack: C# + Windows Forms, with Windows API interop where needed.
- Do not bundle the .NET runtime. Target runtime family/version remains to be decided: modern .NET requires an installed Desktop Runtime; .NET Framework is included with Windows.

## Proposals awaiting agreement

- Standalone modifiers trigger on release only if no other key or mouse operation participated in the gesture.
- First release focuses on layout activation, selection conversion, and settings.

## Open questions

### Technology evaluation (historical alternatives; C# + WinForms selected)

- Earlier proposal: self-contained C# deployment. Superseded by the decision not to bundle the runtime.
- Alternative for a compact native utility: C++ with Win32 and statically linked CRT. More manual UI and resource-management code; requires C++ Build Tools and Windows SDK.
- Rust with Win32 remains possible, but adds Rust tooling alongside the MSVC build prerequisites and does not simplify this small Windows-only UI enough to be the preferred choice.
- Local check: `dotnet` is available, but `dotnet --list-sdks` returned no SDKs. Development SDK installation is still needed for the C# option.
- Decide whether smaller executable/resource usage or simpler development takes priority. Do not promise exact memory or binary size without a prototype.
- Runtime decision pending: preserve zero prerequisite installation by targeting Windows-provided .NET Framework, or permit a separate Desktop Runtime prerequisite for modern .NET. Local Desktop Runtimes include 8.0.8 and 9.0.22, but their presence is not guaranteed on other Windows machines.
- Sources: [single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file), [WinForms tray component](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-windows-forms), [static CRT](https://learn.microsoft.com/en-us/cpp/build/reference/md-mt-ld-use-run-time-library), [Rust installation](https://rust-lang.org/tools/install/).

### Key handling

- Confirm release-based activation and cancellation when a modifier participates in keyboard shortcuts or mouse actions.
- Should a long standalone hold activate the action, or only a short tap?
- Should assigned media keys suppress their original action?
- Support ordinary key combinations alongside single keys?
- Defaults versus requiring explicit user configuration?

### Selection conversion

- One global layout pair, or multiple pairs with separate shortcuts?
- What is the conversion shortcut?
- After conversion, should the active layout change? How should mixed text affect that decision?
- For overlapping alphabets (e.g. English/German), how is conversion direction determined?
- Preserve case, line breaks, and characters outside the selected pair? How should punctuation and ambiguous mappings behave?
- Is temporary clipboard use acceptable? What preservation guarantees are required?
- Is plain-text replacement acceptable in rich-text editors?
- Is conversion of read-only selections required, and where should the result go?

### Compatibility

- Which applications are the initial acceptance targets?
- Are elevated applications, terminals, and Remote Desktop required?
- Are per-application exclusions and a temporary pause needed?

### UI and distribution

- Static tray icon or active-layout indicator?
- Settings UI language: Russian, English, or both?
- Optional startup with Windows?
- Portable settings beside the executable or per-user settings?
- Manual releases or update checking?
- Open-source license?
- Resource usage and executable size preferences?

## Next steps

1. Resolve key and conversion semantics.
2. Agree on initial compatibility targets.
3. Compare technology choices against the accepted requirements, verifying relevant platform constraints.
4. Agree on UI, packaging, licensing, and acceptance criteria.

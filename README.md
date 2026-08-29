# Desktop Automation Lab

Windows desktop automation lab comparing the same Notepad flows with:

- **C# + NUnit + FlaUI + UIA3**
- **Python + pytest + pywinauto (UIA backend)**

The project deliberately treats the execution environment as part of the test result. A desktop UI test passing on a GitHub-hosted Windows Server runner does **not** prove that the same scenario passes on a user's Windows 11 installation with a different Notepad build, UI tree, keyboard layout, session state, or feature set.

## Validation levels

| Level | Environment | What it proves |
| --- | --- | --- |
| GitHub-hosted baseline | `windows-latest` / Windows Server | Basic launch, UI Automation discovery, editing and saving work in that runner image |
| Local Windows validation | Your actual Windows 10/11 desktop | The suite works with your installed Notepad, locale, keyboard layouts and desktop session |
| Self-hosted Windows 11 validation | Dedicated logged-in Windows 11 GitHub runner | CI execution against the same class of real desktop environment as local Windows 11 |

**Rule:** report these results separately. `GitHub-hosted: PASS` must never be presented as `Windows 11 local: PASS` unless the local or self-hosted Windows 11 suite was also executed successfully.

## FlaUI test categories

`NotepadTests.cs` separates tests by environment requirement:

- `Baseline` — portable smoke scenarios intended for both GitHub-hosted and real Windows environments.
- `ModernNotepad` — scenarios that require the modern Windows 11 Notepad feature set, such as formatting.

Current FlaUI scenarios include:

1. Type text and save it.
2. Replace existing text with `Ctrl+A`, save, and verify the filesystem result.
3. Select text and apply H1 + Bold on a modern Notepad build.

Text entry in the baseline FlaUI tests uses **clipboard + Ctrl+V** rather than `Keyboard.Type(string)`. FlaUI's character typing relies on Windows keyboard-layout mapping, so the same Latin text can be typed incorrectly when the active layout is Ukrainian. Clipboard paste keeps the input deterministic while shortcuts such as `Ctrl+A` and `Ctrl+S` remain real keyboard interactions.

## Project structure

```text
desktop-automation-lab/
├── flaui/
│   └── Notepad.Tests/
│       ├── Notepad.Tests.csproj
│       └── NotepadTests.cs
├── python/
│   ├── inspect_notepad.py
│   ├── requirements.txt
│   └── tests/
│       └── test_notepad.py
├── scripts/
│   └── run-local.ps1
└── .github/
    └── workflows/
        ├── desktop-ui-tests.yml
        └── desktop-ui-tests-windows11-self-hosted.yml
```

## Run full validation locally

Requirements:

- Windows 10/11
- .NET 8 SDK
- Python with dependencies from `python/requirements.txt`

Install Python dependencies once:

```powershell
python -m pip install -r python/requirements.txt
```

Then run the complete local suite:

```powershell
.\scripts\run-local.ps1
```

The script prints the environment first, including OS, .NET, Python and installed Notepad package/version when available, then runs:

```powershell
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --configuration Debug
python -m pytest python/tests -vv
```

Run only the FlaUI baseline tests locally:

```powershell
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --filter "TestCategory=Baseline"
```

Run only the modern Notepad formatting tests:

```powershell
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --filter "TestCategory=ModernNotepad"
```

## GitHub-hosted CI

`.github/workflows/desktop-ui-tests.yml` runs automatically for pull requests and `main`.

It intentionally runs **only the FlaUI `Baseline` category** on the GitHub-hosted Windows runner:

```text
GitHub-hosted Windows Server
        ↓
Baseline FlaUI tests
        +
pywinauto tests
```

The workflow also saves environment information with the test artifacts. A green result means only that the baseline passed on that particular runner image.

Modern Windows 11 formatting tests are excluded from this hosted baseline because the runner's Notepad build can differ materially from the current Windows 11 Store/packaged Notepad.

## Full CI on real Windows 11

`.github/workflows/desktop-ui-tests-windows11-self-hosted.yml` is a **manual** workflow for a Windows 11 self-hosted runner labeled:

```text
self-hosted
Windows
desktop-ui
```

It runs the **full** FlaUI suite, including `ModernNotepad`, plus pywinauto.

The self-hosted runner should run in an interactive, logged-in and unlocked Windows desktop session. Desktop UI automation should not be treated like a headless API/unit-test workload.

## Environment diagnostics

FlaUI test output records:

- execution environment label (`local`, GitHub-hosted, self-hosted);
- OS version;
- current keyboard layout where available;
- Notepad process path/version where accessible;
- whether the modern formatting UI was detected.

This makes a local/CI mismatch actionable instead of treating it as an unexplained flaky test.

## UI Automation locator strategy

Notepad implementations differ between Windows versions. The editor locator currently supports both:

```csharp
window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
    ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
```

For other applications, inspect controls with FlaUInspect and prefer stable properties such as:

- `AutomationId`
- `Name`
- `ControlType`
- `ClassName`
- supported patterns (`Value`, `Text`, `Invoke`, `Selection`, etc.)

Prefer a stable `AutomationId` when the application exposes one. Use fallbacks only when the application's UI Automation tree genuinely differs across supported versions.

# Desktop Automation Lab

A small Windows desktop UI automation lab that exercises the same Notepad application with two stacks:

- **C# + NUnit + FlaUI + UIA3**
- **Python + pytest + pywinauto (UIA backend)**

The repository intentionally stays small. It is a practical comparison of desktop automation approaches, CI execution, screenshots, and Allure reporting rather than a generic framework.

## Current test coverage

### FlaUI

`flaui/Notepad.Tests/NotepadTests.cs`

1. Type text, save it, and verify exact file contents.
2. Save initial text, replace it, and verify the replacement exactly.
3. Select/copy text, verify the clipboard, replace the selection, save, and verify the file.

`flaui/Notepad.Tests/NotepadZoomTests.cs`

4. Reset Notepad zoom to 100%, zoom in twice, verify the exposed zoom percentage, then reset to 100%.

Shared Notepad launch, UIA discovery, editor lookup, screenshots, file cleanup, and clipboard helpers live in `NotepadTestBase.cs` so the test fixtures contain mostly scenario logic.

### pywinauto

`python/tests/test_notepad.py`

5. Open a unique Notepad document, paste text independently of the active keyboard layout, read it through UI Automation, save it, and verify the exact file contents.

The Python fixture owns cleanup, so teardown closes the test tab even when a test fails before its final assertion. If the test caused a new Notepad window to be created, teardown also closes the blank window left behind by modern Notepad. A Notepad window that already existed before the test is left open so unrelated user documents are not closed.

## Why input uses the clipboard

Desktop key simulation is affected by the active Windows keyboard layout. A Latin string can be corrupted when the runner or local machine is using a Ukrainian layout.

Both implementations therefore use the Windows clipboard plus `Ctrl+V` for arbitrary text. Keyboard shortcuts such as `Ctrl+A`, `Ctrl+C`, `Ctrl+S`, `Ctrl+W`, and zoom shortcuts remain real keyboard interactions.

## Project structure

```text
desktop-automation-lab/
├── flaui/
│   └── Notepad.Tests/
│       ├── Notepad.Tests.csproj
│       ├── NotepadTestBase.cs
│       ├── NotepadTests.cs
│       ├── NotepadZoomTests.cs
│       └── allureConfig.json
├── python/
│   ├── inspect_notepad.py
│   ├── requirements.txt
│   └── tests/
│       └── test_notepad.py
├── scripts/
│   ├── run-local.ps1
│   └── hetzner/
│       ├── deploy-allure.sh
│       └── setup-allure-host.sh
├── docs/
│   └── allure-report-hosting.md
├── allurerc.mjs
└── .github/workflows/
    └── desktop-ui-tests.yml
```

## Run locally

Requirements:

- Windows 10/11 with an interactive desktop session
- .NET 8 SDK
- Python 3.12+
- Node.js / `npx` for Allure report generation

Install Python dependencies once:

```powershell
python -m pip install -r python/requirements.txt
```

Run the complete local suite:

```powershell
.\scripts\run-local.ps1
```

Or run the stacks separately:

```powershell
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --configuration Debug
python -m pytest python/tests -vv --alluredir=allure-results
```

Inspect the current Notepad UI Automation tree with:

```powershell
python python/inspect_notepad.py
```

The inspector opens a unique temporary document and closes only that document when finished, instead of assuming the `notepad.exe` launcher PID owns the final window.

## CI

`.github/workflows/desktop-ui-tests.yml` runs on:

- pushes to `main`;
- pull requests targeting `main`;
- manual `workflow_dispatch` runs.

The current GitHub-hosted baseline uses `windows-latest` (Windows Server), runs all four FlaUI scenarios plus the pywinauto scenario, captures screenshots for successful tests as well as failures, generates one combined Allure report, and uploads the report/artifacts to GitHub Actions.

A green hosted run proves the scenarios passed on that runner image. It does not claim that every Windows 11 Notepad build exposes the identical UI Automation tree.

## Allure report

All C# and Python results are written into the same flat `allure-results/` directory and rendered by Allure 3.

Locally:

```powershell
npx -y allure@3.16.0 generate allure-results --config ./allurerc.mjs
npx -y allure@3.16.0 open allure-report
```

On `main`, CI also publishes the latest generated report to the configured Hetzner host. Deployment uses release directories and an atomic `site` symlink switch, so a new report does not replace the live directory file-by-file.

See `docs/allure-report-hosting.md` for server details and GitHub variables/secrets.

## UI Automation locator strategy

Notepad implementations differ between Windows versions. The shared FlaUI editor lookup supports both common control types:

```csharp
window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
    ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
```

For real applications, prefer stable application-owned identifiers in roughly this order:

1. `AutomationId`
2. stable semantic `Name`
3. `ControlType`
4. `ClassName`
5. supported UIA patterns such as Value, Text, Invoke, Selection, or Toggle

Fallback locators are appropriate only when supported application versions genuinely expose different UI Automation trees.

## Test design notes

- Test documents use unique temporary filenames to avoid attaching to an unrelated existing Notepad tab.
- Modern Notepad can reuse a process/window and open documents as tabs, so launcher PID alone is not treated as identity.
- Teardown records whether the test window existed before launch. Test-created windows are closed after the test document is removed; pre-existing windows are preserved.
- Functional assertions use exact text/file equality where the scenario replaces the entire document.
- Reporting failures are logged but do not change the functional test result.
- Meaningful state changes use polling instead of arbitrary long sleeps; very short UI-settling waits remain where Windows exposes no reliable state to observe.

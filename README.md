# Desktop Automation Lab

Small Windows desktop automation lab that tests the same Notepad scenario with two stacks:

- **C# + NUnit + FlaUI + UIA3**
- **Python + pytest + pywinauto (UIA backend)**

The purpose is to compare how both tools work with the same Microsoft UI Automation tree.

## Test scenario

Both implementations do the same thing:

1. Launch `notepad.exe`.
2. Find the editor through UI Automation.
3. Enter `Hello desktop automation`.
4. Read the text back through UI Automation.
5. Assert that the text is present.
6. Kill Notepad during teardown so an unsaved-document dialog cannot block the test.

The editor locator supports both common Notepad representations:

- `ControlType.Document` — common in modern Windows Notepad.
- `ControlType.Edit` — common in older/classic versions.

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
└── .github/
    └── workflows/
        └── desktop-ui-tests.yml
```

The Python folder is intentionally named `python`, not `pywinauto`: a repository directory called `pywinauto` would shadow the installed Python package during imports.

## Run FlaUI locally

Requirements:

- Windows 10/11
- .NET 8 SDK

```powershell
dotnet restore flaui/Notepad.Tests/Notepad.Tests.csproj
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj
```

Core locator logic:

```csharp
window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
    ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
```

## Run pywinauto locally

Requirements:

- Windows 10/11
- Python 3.12 recommended

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r python/requirements.txt
python -m pytest python/tests -vv
```

To print Notepad's UI Automation tree:

```powershell
python python/inspect_notepad.py
```

Core locator logic:

```python
for control_type in ("Document", "Edit"):
    spec = window.child_window(control_type=control_type)
```

## CI

`.github/workflows/desktop-ui-tests.yml` runs both implementations on a GitHub-hosted `windows-latest` runner for pull requests, pushes to `main`, and manual runs.

This is intentionally a real GUI smoke test: it launches Notepad on the remote Windows runner rather than mocking the UI layer.

Desktop UI automation is more sensitive to the runner session than API/unit tests. If GitHub-hosted runners do not expose a sufficiently interactive desktop for a future application under test, the same workflow can be moved to a **self-hosted Windows runner with an unlocked logged-in desktop session**.

## What to inspect in FlaUInspect

For any control, pay attention to:

- `AutomationId`
- `Name`
- `ControlType`
- `ClassName`
- supported UI Automation patterns such as `Value`, `Text`, `Invoke`, `Selection`

Prefer a stable `AutomationId` when the application provides one. For Notepad's editor we deliberately use `ControlType` because the exact UI tree differs between Windows versions.

import subprocess
import tempfile
import time
from pathlib import Path

import pytest
from pywinauto import Desktop
from pywinauto.findwindows import ElementNotFoundError
from pywinauto.uia_defines import NoPatternInterfaceError

TEXT = "Hello desktop automation"


@pytest.fixture
def notepad():
    """Launch a uniquely named temp file and discover the real Notepad UIA window.

    Modern Windows 11 Notepad can start through a short-lived launcher process, so
    attaching to the PID returned by Application.start() is unreliable. Instead we
    find the real top-level window in the desktop UI Automation tree by the unique
    temporary file name.
    """
    with tempfile.NamedTemporaryFile(
        prefix="desktop-automation-",
        suffix=".txt",
        delete=False,
    ) as temp_file:
        temp_path = Path(temp_file.name)

    subprocess.Popen(["notepad.exe", str(temp_path)])

    desktop = Desktop(backend="uia")
    window = find_window_for_file(desktop, temp_path.name, timeout=15)
    window.wait("visible enabled", timeout=10)

    yield window, temp_path

    # The test saves before teardown, so close Notepad normally. This avoids
    # Windows 11 restoring an unsaved/crashed Notepad session on the next run.
    try:
        window.close()
        window.wait_not("exists", timeout=5)
    except Exception:
        pass
    finally:
        temp_path.unlink(missing_ok=True)


def find_window_for_file(desktop, file_name: str, timeout: float):
    deadline = time.time() + timeout
    last_titles = []

    while time.time() < deadline:
        last_titles = []
        for candidate in desktop.windows(control_type="Window"):
            try:
                title = candidate.window_text()
                last_titles.append(title)
                if file_name.lower() in title.lower():
                    return candidate
            except Exception:
                continue
        time.sleep(0.25)

    raise RuntimeError(
        f"Could not find Notepad window for '{file_name}'. "
        f"Top-level windows seen: {last_titles}"
    )


def find_editor(window):
    """Return the Notepad text editor across old and modern Windows versions."""
    for control_type in ("Document", "Edit"):
        spec = window.child_window(control_type=control_type)
        if spec.exists(timeout=3):
            return spec.wrapper_object()

    # Useful diagnostic in local and CI logs if Windows changes the UI tree.
    window.print_control_identifiers()
    raise ElementNotFoundError("Could not find Notepad editor (Document/Edit).")


def set_text(editor, text: str) -> None:
    """Prefer UI Automation ValuePattern; fall back to real keyboard input."""
    editor.set_focus()

    try:
        editor.iface_value.SetValue(text)
        return
    except (NoPatternInterfaceError, AttributeError):
        pass

    editor.type_keys("^a{BACKSPACE}")
    editor.type_keys(text, with_spaces=True, pause=0.02)


def read_text(editor) -> str:
    """Read through ValuePattern or TextPattern, depending on the Notepad version."""
    try:
        return editor.iface_value.CurrentValue
    except (NoPatternInterfaceError, AttributeError):
        return editor.iface_text.DocumentRange.GetText(-1).rstrip("\r\n")


def wait_for_saved_text(path: Path, expected: str, timeout: float = 5) -> None:
    deadline = time.time() + timeout

    while time.time() < deadline:
        try:
            if expected in path.read_text(encoding="utf-8"):
                return
        except (OSError, UnicodeDecodeError):
            pass
        time.sleep(0.1)

    actual = path.read_text(encoding="utf-8", errors="replace")
    raise AssertionError(f"Saved file did not contain expected text. Actual: {actual!r}")


def test_can_type_read_and_save_text(notepad):
    window, temp_path = notepad
    editor = find_editor(window)

    set_text(editor, TEXT)
    time.sleep(0.3)
    assert TEXT in read_text(editor)

    # Exercise a real desktop shortcut and verify the side effect on disk.
    editor.set_focus()
    editor.type_keys("^s")
    wait_for_saved_text(temp_path, TEXT)

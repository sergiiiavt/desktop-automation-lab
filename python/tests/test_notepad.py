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
    """Open a unique temp file and discover its real Windows 11 Notepad window."""
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

    # Modern Notepad may reuse one window for several tabs. The window can be the
    # right one while the first Document in its UIA tree belongs to another tab.
    # Explicitly activate the tab that contains our unique temp file first.
    activate_file_tab(window, temp_path.name, timeout=5)

    yield window, temp_path

    temp_path.unlink(missing_ok=True)


def find_window_for_file(desktop, file_name: str, timeout: float):
    """Find the Notepad top-level window and return a WindowSpecification."""
    deadline = time.time() + timeout
    last_titles = []

    while time.time() < deadline:
        last_titles = []
        for candidate in desktop.windows(control_type="Window"):
            try:
                title = candidate.window_text()
                last_titles.append(title)
                if file_name.lower() in title.lower():
                    return desktop.window(handle=candidate.handle)
            except Exception:
                continue
        time.sleep(0.25)

    raise RuntimeError(
        f"Could not find Notepad window for '{file_name}'. "
        f"Top-level windows seen: {last_titles}"
    )


def activate_file_tab(window, file_name: str, timeout: float) -> None:
    """Activate the Notepad tab that belongs to the test temp file."""
    deadline = time.time() + timeout
    expected_names = {file_name.lower(), Path(file_name).stem.lower()}
    last_tabs = []

    while time.time() < deadline:
        last_tabs = []
        root = window.wrapper_object()

        for tab in root.descendants(control_type="TabItem"):
            try:
                title = tab.window_text()
                last_tabs.append(title)
                title_lower = title.lower()

                if any(expected in title_lower for expected in expected_names):
                    tab.click_input()
                    time.sleep(0.25)
                    return
            except Exception:
                continue

        # On classic Notepad there is no tab strip. If the window title itself is
        # already the unique file, there is nothing to activate.
        try:
            window_title = root.window_text().lower()
            if any(expected in window_title for expected in expected_names):
                return
        except Exception:
            pass

        time.sleep(0.25)

    raise RuntimeError(
        f"Could not activate Notepad tab for '{file_name}'. "
        f"TabItems seen: {last_tabs}"
    )


def find_editor(window):
    """Return only the visible editor of the currently active Notepad tab."""
    root = window.wrapper_object()

    for control_type in ("Document", "Edit"):
        for candidate in root.descendants(control_type=control_type):
            try:
                rect = candidate.rectangle()
                if (
                    candidate.is_visible()
                    and candidate.is_enabled()
                    and rect.width() > 0
                    and rect.height() > 0
                ):
                    return candidate
            except Exception:
                continue

    window.print_control_identifiers()
    raise ElementNotFoundError(
        "Could not find a visible Notepad editor (Document/Edit) for the active tab."
    )


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


def close_test_tab(window, file_name: str) -> None:
    """Close only the test tab, not the whole Notepad window/session."""
    activate_file_tab(window, file_name, timeout=3)
    editor = find_editor(window)
    editor.set_focus()
    editor.type_keys("^w")
    time.sleep(0.25)


def test_can_type_read_and_save_text(notepad):
    window, temp_path = notepad

    activate_file_tab(window, temp_path.name, timeout=3)
    editor = find_editor(window)

    set_text(editor, TEXT)
    time.sleep(0.3)
    assert TEXT in read_text(editor)

    # Exercise a real desktop shortcut and verify its side effect on disk.
    editor.set_focus()
    editor.type_keys("^s")
    wait_for_saved_text(temp_path, TEXT)

    # The file is saved, so Ctrl+W can close just this tab without a save prompt.
    close_test_tab(window, temp_path.name)

import time

import pytest
from pywinauto import Application
from pywinauto.findwindows import ElementNotFoundError
from pywinauto.uia_defines import NoPatternInterfaceError

TEXT = "Hello desktop automation"


@pytest.fixture
def notepad():
    app = Application(backend="uia").start("notepad.exe")
    window = app.top_window()
    window.wait("visible enabled", timeout=10)

    yield window

    # Kill instead of close so an unsaved-document dialog cannot block teardown.
    try:
        app.kill()
    except Exception:
        pass


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


def test_can_type_and_read_text(notepad):
    editor = find_editor(notepad)

    set_text(editor, TEXT)
    time.sleep(0.3)

    assert TEXT in read_text(editor)

import os
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

    # Modern Notepad may reuse one window for several tabs. Select the exact
    # temp-file tab through the Tab control's Selection pattern, not a raw click.
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
    """Select the exact Notepad tab and verify that it became selected."""
    deadline = time.time() + timeout
    expected_names = {file_name.lower(), Path(file_name).stem.lower()}
    last_tabs = []

    while time.time() < deadline:
        root = window.wrapper_object()
        last_tabs = []

        for tab_control in root.descendants(control_type="Tab"):
            try:
                texts = tab_control.texts()
                last_tabs.extend(texts)

                matching_index = next(
                    (
                        index
                        for index, title in enumerate(texts)
                        if any(expected in title.lower() for expected in expected_names)
                    ),
                    None,
                )

                if matching_index is None:
                    continue

                tab_control.select(matching_index)
                time.sleep(0.25)

                selected_index = tab_control.get_selected_tab()
                if selected_index == matching_index:
                    return
            except Exception:
                continue

        for tab in root.descendants(control_type="TabItem"):
            try:
                title = tab.window_text()
                last_tabs.append(title)
                if any(expected in title.lower() for expected in expected_names):
                    tab.click_input()
                    time.sleep(0.25)

                    try:
                        if tab.iface_selection_item.CurrentIsSelected:
                            return
                    except Exception:
                        return
            except Exception:
                continue

        try:
            window_title = root.window_text().lower()
            if any(expected in window_title for expected in expected_names):
                return
        except Exception:
            pass

        time.sleep(0.25)

    raise RuntimeError(
        f"Could not activate Notepad tab for '{file_name}'. "
        f"Tabs seen: {last_tabs}"
    )


def find_editor(window):
    """Return the visible editor of the currently selected Notepad tab."""
    root = window.wrapper_object()
    candidates = []

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
                    candidates.append(candidate)
            except Exception:
                continue

        if candidates:
            return max(
                candidates,
                key=lambda item: item.rectangle().width() * item.rectangle().height(),
            )

    window.print_control_identifiers()
    raise ElementNotFoundError(
        "Could not find a visible Notepad editor (Document/Edit) for the active tab."
    )


def set_text(editor, text: str) -> None:
    """Enter text through real keyboard input so Notepad marks the file as dirty."""
    editor.set_focus()
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


def capture_window(window, test_name: str, step: str) -> Path | None:
    """Capture only the Notepad window for the portable HTML/artifact report."""
    try:
        artifacts_root = Path(
            os.environ.get("DESKTOP_TEST_ARTIFACTS_DIR", "python/TestArtifacts")
        ).resolve()
        safe_test_name = "".join(
            char if char.isalnum() or char in "-_." else "_" for char in test_name
        )
        target_dir = artifacts_root / safe_test_name
        target_dir.mkdir(parents=True, exist_ok=True)

        timestamp = time.strftime("%H%M%S")
        target = target_dir / f"{timestamp}-{step}.png"
        window.wrapper_object().capture_as_image().save(target)
        print(f"Screenshot: {target}")
        return target
    except Exception as exc:
        # Reporting should never change the functional test result.
        print(f"Screenshot warning: {exc}")
        return None


def test_can_type_read_and_save_text(notepad, request):
    window, temp_path = notepad
    test_name = request.node.name

    activate_file_tab(window, temp_path.name, timeout=3)
    capture_window(window, test_name, "00-opened")
    editor = find_editor(window)

    set_text(editor, TEXT)
    time.sleep(0.3)
    capture_window(window, test_name, "01-text-entered")
    assert TEXT in read_text(editor)

    editor.set_focus()
    editor.type_keys("^s")
    wait_for_saved_text(temp_path, TEXT)
    capture_window(window, test_name, "02-saved")

    close_test_tab(window, temp_path.name)

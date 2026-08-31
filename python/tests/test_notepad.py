import os
import subprocess
import tempfile
import time
from pathlib import Path

import allure
import pyperclip
import pytest
from pywinauto import Desktop
from pywinauto.findwindows import ElementNotFoundError
from pywinauto.uia_defines import NoPatternInterfaceError

TEST_CASE_ID = "TC0006"
TEXT = f"[{TEST_CASE_ID}] Hello desktop automation"


@pytest.fixture
def notepad(request):
    """Open a unique temp file and restore the desktop after the test."""
    window = None
    temp_path = None
    preexisting_window_handles = set()

    try:
        with tempfile.NamedTemporaryFile(
            prefix="desktop-automation-",
            suffix=".txt",
            delete=False,
        ) as temp_file:
            temp_path = Path(temp_file.name)

        desktop = Desktop(backend="uia")
        preexisting_window_handles = {
            candidate.handle
            for candidate in desktop.windows(control_type="Window")
            if candidate.handle
        }

        subprocess.Popen(["notepad.exe", str(temp_path)])

        window = find_window_for_file(desktop, temp_path.name, timeout=15)
        window.wait("visible enabled", timeout=10)
        activate_file_tab(window, temp_path.name, timeout=5)

        yield window, temp_path
    finally:
        if window is not None and temp_path is not None:
            capture_window(window, request.node.name, "99-final")
            try:
                close_test_tab(window, temp_path.name)
                if window.handle not in preexisting_window_handles:
                    close_test_created_window(window)
            except Exception as exc:
                print(f"Cleanup warning: {exc}")

        if temp_path is not None:
            temp_path.unlink(missing_ok=True)


def find_window_for_file(desktop, file_name: str, timeout: float):
    """Find the Notepad top-level window for the unique test file."""
    deadline = time.monotonic() + timeout
    last_titles = []

    while time.monotonic() < deadline:
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
    """Select the exact Notepad tab, with a classic-Notepad title fallback."""
    deadline = time.monotonic() + timeout
    expected_names = {file_name.lower(), Path(file_name).stem.lower()}
    last_tabs = []

    while time.monotonic() < deadline:
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
                time.sleep(0.15)
                if tab_control.get_selected_tab() == matching_index:
                    return
            except Exception:
                continue

        for tab in root.descendants(control_type="TabItem"):
            try:
                title = tab.window_text()
                last_tabs.append(title)
                if any(expected in title.lower() for expected in expected_names):
                    tab.click_input()
                    time.sleep(0.15)
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

        time.sleep(0.2)

    raise RuntimeError(
        f"Could not activate Notepad tab for '{file_name}'. Tabs seen: {last_tabs}"
    )


def find_editor(window):
    """Return the largest visible Document/Edit element in the active tab."""
    root = window.wrapper_object()

    for control_type in ("Document", "Edit"):
        candidates = []
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
    """Paste deterministic text so the active keyboard layout cannot corrupt it."""
    previous_clipboard_text = ""
    try:
        previous_clipboard_text = pyperclip.paste()
    except pyperclip.PyperclipException:
        pass

    editor.set_focus()
    editor.type_keys("^a")
    pyperclip.copy(text)
    editor.type_keys("^v")

    wait_until(
        lambda: read_text(editor) == text,
        timeout=3,
        failure=f"Editor did not become exactly {text!r}",
    )

    try:
        pyperclip.copy(previous_clipboard_text)
    except pyperclip.PyperclipException:
        pass


def read_text(editor) -> str:
    """Read through ValuePattern or TextPattern, depending on Notepad version."""
    try:
        value = editor.iface_value.CurrentValue
    except (NoPatternInterfaceError, AttributeError):
        value = editor.iface_text.DocumentRange.GetText(-1)
    return value.rstrip("\r\n")


def wait_for_saved_text(path: Path, expected: str, timeout: float = 5) -> None:
    def saved_exactly() -> bool:
        try:
            return path.read_text(encoding="utf-8") == expected
        except (OSError, UnicodeDecodeError):
            return False

    wait_until(
        saved_exactly,
        timeout=timeout,
        failure=f"Saved file did not become exactly {expected!r}",
    )


def wait_until(condition, timeout: float, failure: str, interval: float = 0.1) -> None:
    deadline = time.monotonic() + timeout
    last_error = None

    while time.monotonic() < deadline:
        try:
            if condition():
                return
        except Exception as exc:
            last_error = exc
        time.sleep(interval)

    suffix = f" Last error: {last_error}" if last_error else ""
    raise AssertionError(f"{failure}.{suffix}")


def close_test_tab(window, file_name: str) -> None:
    """Close the exact test tab without touching unrelated Notepad documents."""
    activate_file_tab(window, file_name, timeout=3)
    editor = find_editor(window)
    editor.set_focus()
    editor.type_keys("^w")
    time.sleep(0.2)


def close_test_created_window(window) -> None:
    """Close the remaining blank window only when this test created that window."""
    try:
        window.close()
    except Exception:
        try:
            root = window.wrapper_object()
            root.set_focus()
            root.type_keys("%{F4}")
        except Exception:
            return

    deadline = time.monotonic() + 3
    while time.monotonic() < deadline:
        try:
            if not window.exists(timeout=0.1):
                return
        except Exception:
            return
        time.sleep(0.1)

    print(f"Cleanup warning: test-created Notepad window {window.handle} did not close")


def capture_window(window, test_name: str, step: str) -> Path | None:
    """Capture the Notepad window and attach it directly to Allure."""
    try:
        artifacts_root = Path(
            os.environ.get("DESKTOP_TEST_ARTIFACTS_DIR", "python/TestArtifacts")
        ).resolve()
        safe_test_name = "".join(
            char if char.isalnum() or char in "-_." else "_" for char in test_name
        )
        target_dir = artifacts_root / safe_test_name
        target_dir.mkdir(parents=True, exist_ok=True)

        target = target_dir / f"{time.time_ns()}-{step}.png"
        image = window.wrapper_object().capture_as_image()
        image.save(target)

        allure.attach(
            target.read_bytes(),
            name=f"{TEST_CASE_ID} | Notepad - {step}",
            attachment_type=allure.attachment_type.PNG,
        )

        print(f"Screenshot: {target}")
        return target
    except Exception as exc:
        print(f"Screenshot warning: {exc}")
        return None


@allure.feature("Notepad desktop automation")
@allure.suite("Python - Notepad")
@allure.title("TC0006 | Can type, read and save text in Notepad")
@allure.label("testCaseId", TEST_CASE_ID)
@allure.tag(TEST_CASE_ID)
def test_TC0006_can_type_read_and_save_text(notepad, request):
    print(f"Test case: {TEST_CASE_ID} | Can type, read and save text in Notepad")
    window, temp_path = notepad
    test_name = request.node.name

    with allure.step(f"{TEST_CASE_ID} | Open the exact Notepad test tab"):
        activate_file_tab(window, temp_path.name, timeout=3)
        capture_window(window, test_name, "00-opened")
        editor = find_editor(window)

    with allure.step(f"{TEST_CASE_ID} | Paste layout-independent text into Notepad"):
        set_text(editor, TEXT)
        capture_window(window, test_name, "01-text-entered")
        assert read_text(editor) == TEXT

    with allure.step(f"{TEST_CASE_ID} | Save with Ctrl+S and verify exact file contents"):
        editor.set_focus()
        editor.type_keys("^s")
        wait_for_saved_text(temp_path, TEXT)
        capture_window(window, test_name, "02-saved")
        assert temp_path.read_text(encoding="utf-8") == TEXT

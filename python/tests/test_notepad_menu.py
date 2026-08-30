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

TEST_CASE_ID = "TC0007"
VISIBLE_TEXT = "[TC0007] pywinauto portable menu test"


@pytest.fixture
def notepad_menu(request):
    window = None
    temp_path = None

    try:
        with tempfile.NamedTemporaryFile(
            prefix="desktop-automation-menu-python-",
            suffix=".txt",
            delete=False,
            mode="w",
            encoding="utf-8",
        ) as temp_file:
            temp_file.write(VISIBLE_TEXT)
            temp_path = Path(temp_file.name)

        desktop = Desktop(backend="uia")
        subprocess.Popen(["notepad.exe", str(temp_path)])
        window = find_window_for_file(desktop, temp_path.name, timeout=15)
        window.wait("visible enabled", timeout=10)
        capture_window(window, request.node.name, "00-opened")
        yield desktop, window, temp_path
    finally:
        if window is not None:
            try:
                editor = find_editor(window)
                editor.set_focus()
                editor.type_keys("^w")
                time.sleep(0.25)
            except Exception as exc:
                print(f"Cleanup warning: {exc}")

            try:
                if window.exists(timeout=0.2):
                    window.close()
            except Exception:
                pass

        if temp_path is not None:
            temp_path.unlink(missing_ok=True)


def find_window_for_file(desktop, file_name: str, timeout: float):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for candidate in desktop.windows(control_type="Window"):
            try:
                if file_name.lower() in candidate.window_text().lower():
                    return desktop.window(handle=candidate.handle)
            except Exception:
                continue
        time.sleep(0.25)
    raise RuntimeError(f"Could not find Notepad window for '{file_name}'.")


def find_editor(window):
    root = window.wrapper_object()
    for control_type in ("Document", "Edit"):
        candidates = []
        for candidate in root.descendants(control_type=control_type):
            try:
                rect = candidate.rectangle()
                if candidate.is_visible() and rect.width() > 0 and rect.height() > 0:
                    candidates.append(candidate)
            except Exception:
                continue
        if candidates:
            return max(
                candidates,
                key=lambda item: item.rectangle().width() * item.rectangle().height(),
            )
    raise ElementNotFoundError("Could not find visible Notepad editor.")


def read_text(editor) -> str:
    try:
        value = editor.iface_value.CurrentValue
    except (NoPatternInterfaceError, AttributeError):
        value = editor.iface_text.DocumentRange.GetText(-1)
    return value.rstrip("\r\n")


def click_visible_menu_item(desktop, name: str, timeout: float = 4) -> None:
    item = wait_for_menu_item(desktop, name, timeout)
    rect = item.rectangle()
    print(
        f"Physical menu click: '{name}' at "
        f"({rect.mid_point().x}, {rect.mid_point().y})"
    )
    item.click_input()
    time.sleep(0.35)


def click_menu_command(desktop, window, test_name: str, menu_name: str, command_name: str, step: str) -> None:
    click_visible_menu_item(desktop, menu_name)
    capture_window(window, test_name, step)
    click_visible_menu_item(desktop, command_name)


def wait_for_menu_item(desktop, name: str, timeout: float):
    deadline = time.monotonic() + timeout
    seen = []

    while time.monotonic() < deadline:
        seen = []
        for top_level in desktop.windows(control_type="Window"):
            try:
                root = top_level.wrapper_object()
                for item in root.descendants(control_type="MenuItem"):
                    try:
                        title = item.window_text().strip()
                        if title:
                            seen.append(title)
                        if title.casefold() == name.casefold() and item.is_visible():
                            return item
                    except Exception:
                        continue
            except Exception:
                continue
        time.sleep(0.1)

    raise AssertionError(
        f"Visible menu item {name!r} was not found. Menu items seen: {seen[-30:]}"
    )


def wait_until(condition, timeout: float, failure: str, interval: float = 0.1) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            if condition():
                return
        except Exception:
            pass
        time.sleep(interval)
    raise AssertionError(failure)


def normalize_newlines(value: str) -> str:
    return value.replace("\r\n", "\n").replace("\r", "\n")


def capture_window(window, test_name: str, step: str) -> None:
    try:
        artifacts_root = Path(
            os.environ.get("DESKTOP_TEST_ARTIFACTS_DIR", "python/TestArtifacts")
        ).resolve()
        target_dir = artifacts_root / test_name
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
    except Exception as exc:
        print(f"Screenshot warning: {exc}")


@allure.feature("Notepad desktop automation")
@allure.suite("Python - Notepad")
@allure.title("TC0007 | Can copy and paste through visible menu clicks")
@allure.label("testCaseId", TEST_CASE_ID)
@allure.tag(TEST_CASE_ID, "menu", "mouse", "portable")
def test_TC0007_can_copy_and_paste_through_visible_menu_clicks(notepad_menu, request):
    desktop, window, _ = notepad_menu
    test_name = request.node.name
    editor = find_editor(window)

    assert read_text(editor) == VISIBLE_TEXT
    capture_window(window, test_name, "01-before-menu-clicks")

    with allure.step(f"{TEST_CASE_ID} | Physically click Edit > Select all"):
        click_menu_command(
            desktop,
            window,
            test_name,
            "Edit",
            "Select all",
            "02-edit-menu-open-before-select-all",
        )

    with allure.step(f"{TEST_CASE_ID} | Physically click Edit > Copy"):
        click_menu_command(
            desktop,
            window,
            test_name,
            "Edit",
            "Copy",
            "03-edit-menu-open-before-copy",
        )
        wait_until(
            lambda: pyperclip.paste() == VISIBLE_TEXT,
            timeout=3,
            failure=f"Edit > Copy did not place {VISIBLE_TEXT!r} on the clipboard.",
        )
        assert pyperclip.paste() == VISIBLE_TEXT
        capture_window(window, test_name, "04-copied-through-menu")

    editor.set_focus()
    editor.type_keys("{END}{ENTER}")
    time.sleep(0.2)

    with allure.step(f"{TEST_CASE_ID} | Physically click Edit > Paste"):
        click_menu_command(
            desktop,
            window,
            test_name,
            "Edit",
            "Paste",
            "05-edit-menu-open-before-paste",
        )

    expected = VISIBLE_TEXT + "\n" + VISIBLE_TEXT
    wait_until(
        lambda: normalize_newlines(read_text(editor)) == expected,
        timeout=3,
        failure="Edit > Paste did not append the copied text on a new line.",
    )
    capture_window(window, test_name, "06-pasted-through-menu")

    assert normalize_newlines(read_text(editor)) == expected

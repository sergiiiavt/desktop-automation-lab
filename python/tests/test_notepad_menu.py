import os
import subprocess
import tempfile
import time
from pathlib import Path

import allure
import pytest
from pywinauto import Desktop
from pywinauto.findwindows import ElementNotFoundError
from pywinauto.uia_defines import NoPatternInterfaceError

TEST_CASE_ID = "TC0007"
VISIBLE_TEXT = (
    "[TC0007] pywinauto menu test - watch View > Zoom > Zoom in being clicked"
)


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
                editor.type_keys("^0")
                time.sleep(0.15)
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
    time.sleep(0.45)


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


def rendered_text_size(editor):
    try:
        values = list(editor.iface_text.DocumentRange.GetBoundingRectangles())
    except Exception:
        return None

    if not values:
        return None

    # UI Automation returns a flattened sequence: left, top, width, height, ...
    if all(isinstance(value, (int, float)) for value in values):
        rectangles = [values[index : index + 4] for index in range(0, len(values), 4)]
        rectangles = [rect for rect in rectangles if len(rect) == 4 and rect[2] > 0 and rect[3] > 0]
        if not rectangles:
            return None
        left = min(rect[0] for rect in rectangles)
        top = min(rect[1] for rect in rectangles)
        right = max(rect[0] + rect[2] for rect in rectangles)
        bottom = max(rect[1] + rect[3] for rect in rectangles)
        return right - left, bottom - top

    return None


def wait_for_larger_text(editor, baseline, timeout: float = 4):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        last = rendered_text_size(editor)
        if last and (last[0] > baseline[0] * 1.05 or last[1] > baseline[1] * 1.05):
            return last
        time.sleep(0.1)
    raise AssertionError(
        f"Zoom in menu command was clicked but rendered text did not grow. "
        f"Before={baseline}; last={last}"
    )


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
@allure.title("TC0007 | Can zoom in through visible menu clicks")
@allure.label("testCaseId", TEST_CASE_ID)
@allure.tag(TEST_CASE_ID, "menu", "mouse")
def test_TC0007_can_zoom_in_through_visible_menu_clicks(notepad_menu, request):
    desktop, window, _ = notepad_menu
    test_name = request.node.name
    editor = find_editor(window)

    assert read_text(editor) == VISIBLE_TEXT

    editor.set_focus()
    editor.type_keys("^0")
    time.sleep(0.2)
    baseline = rendered_text_size(editor)
    assert baseline is not None, "TextPattern bounds are required to verify zoom visually."
    capture_window(window, test_name, "01-before-menu-clicks")

    with allure.step(f"{TEST_CASE_ID} | Physically click View"):
        click_visible_menu_item(desktop, "View")
        capture_window(window, test_name, "02-view-menu-open")

    with allure.step(f"{TEST_CASE_ID} | Physically click Zoom"):
        click_visible_menu_item(desktop, "Zoom")
        capture_window(window, test_name, "03-zoom-submenu-open")

    with allure.step(f"{TEST_CASE_ID} | Physically click Zoom in"):
        click_visible_menu_item(desktop, "Zoom in")
        enlarged = wait_for_larger_text(editor, baseline)
        capture_window(window, test_name, "04-after-zoom-in-menu-click")

    assert enlarged[0] > baseline[0] * 1.05 or enlarged[1] > baseline[1] * 1.05
    assert read_text(editor) == VISIBLE_TEXT

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
from pywinauto.keyboard import send_keys
from pywinauto.uia_defines import NoPatternInterfaceError

TEST_CASE_ID = "TC0007"
VISIBLE_TEXT = "[TC0007] pywinauto locale-independent menu test"
PASTE_TEXT = "[TC0007] pasted after opening the visible Edit menu"


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


def find_second_top_menu_control(window, editor):
    """Find the second left-side top menu control by geometry, not localized text."""
    root = window.wrapper_object()
    window_rect = root.rectangle()
    editor_top = editor.rectangle().top
    max_left = window_rect.left + min(430, int(window_rect.width() * 0.40))
    candidates = []

    for item in root.descendants():
        try:
            control_type = item.element_info.control_type or ""
            if control_type not in {"Button", "MenuItem"}:
                continue
            if not item.is_visible() or not item.is_enabled():
                continue

            rect = item.rectangle()
            center_y = (rect.top + rect.bottom) // 2
            if rect.width() <= 0 or rect.height() <= 0:
                continue
            if center_y <= window_rect.top + 28 or center_y >= editor_top:
                continue
            if rect.left >= max_left:
                continue

            candidates.append(item)
        except Exception:
            continue

    candidates.sort(key=lambda item: (item.rectangle().left, item.rectangle().top))
    if len(candidates) < 2:
        description = [
            f"{getattr(item.element_info, 'control_type', '')}:"
            f"{getattr(item.element_info, 'name', '')!r}@{item.rectangle()}"
            for item in candidates
        ]
        raise AssertionError(
            "Could not locate the first two top-level Notepad menu controls by geometry. "
            f"Candidates: {description}"
        )

    return candidates[1]


def open_second_top_menu(window, editor) -> None:
    """Physically click the second top menu (Edit/Редагувати/etc.)."""
    item = find_second_top_menu_control(window, editor)
    rect = item.rectangle()
    name = (item.window_text() or "<localized second menu>").strip()
    print(
        f"Physical top-menu click: {name!r} at "
        f"({rect.mid_point().x}, {rect.mid_point().y})"
    )
    item.click_input()
    time.sleep(0.45)


def send_ctrl_v_to_active_notepad() -> None:
    """Send physical Ctrl+V to the active Notepad while its Edit menu is open.

    Native Notepad popup menus are visibly rendered on both modern Windows 11
    and classic runner Notepad, but pywinauto/UIA does not expose their child
    commands or AcceleratorKey consistently. Sending the accelerator to the
    already-open menu exercises the same user command without relying on popup
    introspection or localized labels.
    """
    print("Keyboard command while visible menu is open: Ctrl+V")
    send_keys(
        "{VK_CONTROL down}v{VK_CONTROL up}",
        pause=0.06,
        with_spaces=True,
        vk_packet=False,
    )
    time.sleep(0.35)


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
@allure.title("TC0007 | Can use a locale-independent visible Edit menu")
@allure.label("testCaseId", TEST_CASE_ID)
@allure.tag(TEST_CASE_ID, "menu", "mouse", "portable", "locale-independent")
def test_TC0007_can_use_locale_independent_visible_edit_menu(notepad_menu, request):
    _, window, _ = notepad_menu
    test_name = request.node.name
    editor = find_editor(window)

    assert read_text(editor) == VISIBLE_TEXT
    capture_window(window, test_name, "01-before-menu-click")

    pyperclip.copy(PASTE_TEXT)
    editor.set_focus()
    editor.type_keys("{END}{ENTER}")
    time.sleep(0.2)

    with allure.step(
        f"{TEST_CASE_ID} | Physically open the localized Edit menu, then execute Ctrl+V"
    ):
        open_second_top_menu(window, editor)
        capture_window(window, test_name, "02-localized-edit-menu-open")
        send_ctrl_v_to_active_notepad()

    expected = VISIBLE_TEXT + "\n" + PASTE_TEXT
    wait_until(
        lambda: normalize_newlines(read_text(editor)) == expected,
        timeout=3,
        failure=(
            "The Edit menu opened, but Ctrl+V did not paste the expected text into "
            f"Notepad. Actual editor text: {read_text(editor)!r}"
        ),
    )
    capture_window(window, test_name, "03-pasted-after-visible-menu")

    assert normalize_newlines(read_text(editor)) == expected

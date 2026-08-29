import subprocess
import tempfile
import time
from pathlib import Path

from pywinauto import Desktop


def find_notepad_window(file_name: str, timeout: float = 15):
    desktop = Desktop(backend="uia")
    deadline = time.monotonic() + timeout

    while time.monotonic() < deadline:
        for candidate in desktop.windows(control_type="Window"):
            try:
                if file_name.lower() in candidate.window_text().lower():
                    return desktop.window(handle=candidate.handle)
            except Exception:
                continue
        time.sleep(0.25)

    raise RuntimeError(f"Could not find Notepad window for {file_name!r}")


def main() -> None:
    with tempfile.NamedTemporaryFile(
        prefix="desktop-automation-inspect-",
        suffix=".txt",
        delete=False,
    ) as temp_file:
        temp_path = Path(temp_file.name)

    window = None
    try:
        subprocess.Popen(["notepad.exe", str(temp_path)])
        window = find_notepad_window(temp_path.name)
        window.wait("visible enabled", timeout=10)

        print("\n=== Notepad UI Automation tree ===\n")
        window.print_control_identifiers()
        input("\nPress Enter to close only this test document... ")
    finally:
        if window is not None:
            try:
                window.wrapper_object().type_keys("^w")
            except Exception as exc:
                print(f"Cleanup warning: {exc}")
        temp_path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()

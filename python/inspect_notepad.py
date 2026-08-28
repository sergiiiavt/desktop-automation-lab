from pywinauto import Application


app = Application(backend="uia").start("notepad.exe")
window = app.top_window()
window.wait("visible enabled", timeout=10)

print("\n=== Notepad UI Automation tree ===\n")
window.print_control_identifiers()

input("\nPress Enter to close Notepad... ")
app.kill()

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Notepad.Tests;

[TestFixture]
[NonParallelizable]
public class NotepadTests
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    private UIA3Automation? _automation;
    private Window? _window;
    private int? _notepadProcessId;
    private string? _tempFilePath;

    [SetUp]
    public void SetUp()
    {
        _automation = new UIA3Automation();

        // Give this test run its own real file. This makes the target Notepad window
        // identifiable even on modern Windows 11, where notepad.exe may only be a
        // short-lived launcher and Notepad may restore previous tabs/sessions.
        _tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"desktop-automation-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_tempFilePath, string.Empty);

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{_tempFilePath}\"",
            UseShellExecute = true
        })?.Dispose();

        var expectedFileName = Path.GetFileName(_tempFilePath);

        var windowResult = Retry.WhileNull(
            () => FindNotepadWindow(_automation, expectedFileName),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(250),
            ignoreException: true);

        if (!windowResult.Success || windowResult.Result is null)
        {
            throw new InvalidOperationException(
                $"Could not find the Notepad window for '{expectedFileName}' through UI Automation.\n\n" +
                BuildDesktopDiagnostic(_automation));
        }

        _window = windowResult.Result;
        _notepadProcessId = _window.Properties.ProcessId.Value;
    }

    [TearDown]
    public void TearDown()
    {
        // Prefer a normal close. Every test saves the temporary file before teardown,
        // so there should be no Save dialog and Windows 11 Notepad will not treat the
        // test as a crashed/unsaved session to restore on the next run.
        try
        {
            _window?.Close();
            Thread.Sleep(300);
        }
        catch
        {
            // Fall through to process cleanup below.
        }

        try
        {
            if (_notepadProcessId is int processId)
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        finally
        {
            _automation?.Dispose();

            if (_tempFilePath is not null)
            {
                try
                {
                    File.Delete(_tempFilePath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    [Test]
    public void CanTypeAndSaveText()
    {
        const string expected = "Hello desktop automation";

        var editor = FindEditor(_window!);
        ReplaceTextWithKeyboard(editor, expected);

        Assert.That(editor.Text, Does.Contain(expected));

        SaveWithKeyboard();

        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(expected));
    }

    [Test]
    public void CanReplaceExistingTextAndSave()
    {
        const string initialText = "Initial desktop text";
        const string replacementText = "Replaced by FlaUI";

        var editor = FindEditor(_window!);

        ReplaceTextWithKeyboard(editor, initialText);
        SaveWithKeyboard();
        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(initialText));

        ReplaceTextWithKeyboard(editor, replacementText);

        Assert.That(editor.Text, Does.Contain(replacementText));
        Assert.That(editor.Text, Does.Not.Contain(initialText));

        SaveWithKeyboard();

        var savedText = File.ReadAllText(_tempFilePath!);
        Assert.That(savedText, Does.Contain(replacementText));
        Assert.That(savedText, Does.Not.Contain(initialText));
    }

    [Test]
    [Category("Formatting")]
    public void CanFormatSelectedTextAsHeading1AndBold()
    {
        const string text = "Formatted desktop heading";

        if (!HasModernFormattingUi(_window!))
        {
            Assert.Ignore("This Notepad build does not expose the modern formatting UI.");
        }

        var editor = FindEditor(_window!);
        ReplaceTextWithKeyboard(editor, text);

        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);

        var altKey = (VirtualKeyShort)0x12;
        Keyboard.TypeSimultaneously(
            VirtualKeyShort.CONTROL,
            altKey,
            VirtualKeyShort.KEY_1);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
        Thread.Sleep(500);

        SaveWithKeyboard();

        var savedText = File.ReadAllText(_tempFilePath!);
        Assert.That(savedText, Does.Contain(text));
        Assert.That(savedText.TrimStart(), Does.StartWith("# "));
        Assert.That(savedText, Does.Contain($"**{text}**"));
    }

    private static void ReplaceTextWithKeyboard(TextBox editor, string text)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);

        // FlaUI Keyboard.Type(string) internally uses VkKeyScan, which depends on
        // the active Windows keyboard layout. Send Unicode input directly so the
        // same test types the same text under EN, UA and other layouts.
        TypeUnicodeText(text);
        Thread.Sleep(300);
    }

    private static void TypeUnicodeText(string text)
    {
        foreach (var character in text)
        {
            var inputs = new[]
            {
                CreateUnicodeInput(character, keyUp: false),
                CreateUnicodeInput(character, keyUp: true)
            };

            var sent = SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeInput>());

            if (sent != inputs.Length)
            {
                throw new InvalidOperationException(
                    $"SendInput failed while typing Unicode text. Win32 error: {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private static NativeInput CreateUnicodeInput(char character, bool keyUp)
    {
        return new NativeInput
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = KeyEventFUnicode | (keyUp ? KeyEventFKeyUp : 0),
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static void SaveWithKeyboard()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Thread.Sleep(500);
    }

    private static bool HasModernFormattingUi(Window window)
    {
        var formattingNames = new[]
        {
            "H1",
            "Bold",
            "Жирний",
            "Напівжирний",
            "Назва"
        };

        try
        {
            return window.FindAllDescendants()
                .Any(element =>
                    formattingNames.Any(name =>
                        string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static Window? FindNotepadWindow(UIA3Automation automation, string expectedFileName)
    {
        var desktop = automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        foreach (var element in windows)
        {
            try
            {
                var processId = element.Properties.ProcessId.Value;
                using var process = Process.GetProcessById(processId);

                if (!string.Equals(process.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var windowName = element.Name ?? string.Empty;
                if (windowName.Contains(expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return element.AsWindow();
                }
            }
            catch (ArgumentException)
            {
                // Window/process disappeared while enumerating the desktop.
            }
            catch (InvalidOperationException)
            {
                // Window/process disappeared while enumerating the desktop.
            }
        }

        return null;
    }

    private static string BuildDesktopDiagnostic(UIA3Automation automation)
    {
        var lines = new List<string> { "Top-level UI Automation windows:" };

        try
        {
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

            foreach (var element in windows)
            {
                var name = element.Name ?? "<no name>";
                var processId = element.Properties.ProcessId.Value;
                var processName = "<unknown>";

                try
                {
                    using var process = Process.GetProcessById(processId);
                    processName = process.ProcessName;
                }
                catch
                {
                    // Diagnostic only.
                }

                lines.Add($"- '{name}' | PID {processId} | {processName}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"Could not enumerate desktop windows: {ex.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static TextBox FindEditor(Window window)
    {
        var result = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
                  ?? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(250),
            ignoreException: true);

        if (!result.Success || result.Result is null)
        {
            throw new InvalidOperationException("Could not find the Notepad editor through UI Automation.");
        }

        return result.Result.AsTextBox();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        NativeInput[] inputs,
        int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}

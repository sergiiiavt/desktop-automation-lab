using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using NUnit.Framework;
using FlaUITextBox = FlaUI.Core.AutomationElements.TextBox;
using WinFormsClipboard = System.Windows.Forms.Clipboard;
using WinFormsInputLanguage = System.Windows.Forms.InputLanguage;

namespace Notepad.Tests;

[TestFixture]
[NonParallelizable]
public class NotepadTests
{
    private UIA3Automation? _automation;
    private Window? _window;
    private int? _notepadProcessId;
    private string? _tempFilePath;

    [SetUp]
    public void SetUp()
    {
        _automation = new UIA3Automation();

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
        WriteEnvironmentDiagnostics();
    }

    [TearDown]
    public void TearDown()
    {
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
    [Category("Baseline")]
    public void CanTypeAndSaveText()
    {
        const string expected = "Hello desktop automation";

        var editor = FindEditor(_window!);
        ReplaceTextWithClipboard(editor, expected);

        Assert.That(editor.Text, Does.Contain(expected));

        SaveWithKeyboard();

        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(expected));
    }

    [Test]
    [Category("Baseline")]
    public void CanReplaceExistingTextAndSave()
    {
        const string initialText = "Initial desktop text";
        const string replacementText = "Replaced by FlaUI";

        var editor = FindEditor(_window!);

        ReplaceTextWithClipboard(editor, initialText);
        SaveWithKeyboard();
        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(initialText));

        ReplaceTextWithClipboard(editor, replacementText);

        Assert.That(editor.Text, Does.Contain(replacementText));
        Assert.That(editor.Text, Does.Not.Contain(initialText));

        SaveWithKeyboard();

        var savedText = File.ReadAllText(_tempFilePath!);
        Assert.That(savedText, Does.Contain(replacementText));
        Assert.That(savedText, Does.Not.Contain(initialText));
    }

    [Test]
    [Category("ModernNotepad")]
    public void CanFormatSelectedTextAsHeading1AndBold()
    {
        const string text = "Formatted desktop heading";

        if (!HasModernFormattingUi(_window!))
        {
            Assert.Ignore(
                "Modern Notepad formatting UI was not detected in this environment. " +
                "A GitHub-hosted baseline result does not validate Windows 11 formatting behavior.");
        }

        var editor = FindEditor(_window!);
        ReplaceTextWithClipboard(editor, text);

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

    private static void ReplaceTextWithClipboard(FlaUITextBox editor, string text)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);

        // FlaUI Keyboard.Type(string) uses keyboard-layout mapping. Clipboard + Ctrl+V
        // keeps text deterministic across EN, UA and hosted runner layouts while still
        // exercising a real desktop paste action.
        SetClipboardTextSta(text);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Thread.Sleep(300);
    }

    private static void SetClipboardTextSta(string text)
    {
        Exception? clipboardError = null;

        var thread = new Thread(() =>
        {
            try
            {
                WinFormsClipboard.SetText(text);
            }
            catch (Exception ex)
            {
                clipboardError = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (clipboardError is not null)
        {
            throw new InvalidOperationException("Could not set Windows clipboard for desktop test input.", clipboardError);
        }
    }

    private static void SaveWithKeyboard()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Thread.Sleep(500);
    }

    private void WriteEnvironmentDiagnostics()
    {
        var executionEnvironment =
            Environment.GetEnvironmentVariable("DESKTOP_TEST_ENVIRONMENT") ?? "local-or-unspecified";

        TestContext.Progress.WriteLine("=== Desktop test environment ===");
        TestContext.Progress.WriteLine($"Execution: {executionEnvironment}");
        TestContext.Progress.WriteLine($"OS: {Environment.OSVersion}");
        TestContext.Progress.WriteLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");

        try
        {
            TestContext.Progress.WriteLine(
                $"Keyboard layout: {WinFormsInputLanguage.CurrentInputLanguage.Culture.Name}");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Keyboard layout: unavailable ({ex.Message})");
        }

        try
        {
            if (_notepadProcessId is int processId)
            {
                using var process = Process.GetProcessById(processId);
                var version = process.MainModule?.FileVersionInfo.FileVersion ?? "unknown";
                var path = process.MainModule?.FileName ?? "unknown";
                TestContext.Progress.WriteLine($"Notepad PID: {processId}");
                TestContext.Progress.WriteLine($"Notepad version: {version}");
                TestContext.Progress.WriteLine($"Notepad path: {path}");
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Notepad version/path: unavailable ({ex.Message})");
        }

        TestContext.Progress.WriteLine(
            $"Modern formatting UI detected: {HasModernFormattingUi(_window!)}");
        TestContext.Progress.WriteLine("================================");
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

    private static FlaUITextBox FindEditor(Window window)
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
}

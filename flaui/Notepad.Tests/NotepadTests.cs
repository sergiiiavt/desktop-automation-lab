using System.Diagnostics;
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

        // First user action: enter and save initial content.
        ReplaceTextWithKeyboard(editor, initialText);
        SaveWithKeyboard();
        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(initialText));

        // Second user action: Ctrl+A selects existing content, typing replaces it.
        ReplaceTextWithKeyboard(editor, replacementText);

        // Keep these as separate assertions. NUnit 4 exposes two Assert.Multiple
        // delegate overloads, and an untyped lambda can be ambiguous at compile time.
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

        // Select all content, apply Heading 1 with Ctrl+Alt+1, then toggle Bold with Ctrl+B.
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);

        // VK_MENU (0x12) is the Win32 virtual-key value for Alt. FlaUI's
        // VirtualKeyShort enum used by this project does not expose a MENU member.
        var altKey = (VirtualKeyShort)0x12;
        Keyboard.TypeSimultaneously(
            VirtualKeyShort.CONTROL,
            altKey,
            VirtualKeyShort.KEY_1);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
        Thread.Sleep(500);

        SaveWithKeyboard();

        var savedText = File.ReadAllText(_tempFilePath!);

        // The formatting must not lose the selected text. On modern Notepad builds
        // that persist formatting as Markdown, H1 and bold are represented by '# '
        // and '**...**'.
        Assert.That(savedText, Does.Contain(text));
        Assert.That(savedText.TrimStart(), Does.StartWith("# "));
        Assert.That(savedText, Does.Contain($"**{text}**"));
    }

    private static void ReplaceTextWithKeyboard(TextBox editor, string text)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
        Thread.Sleep(300);
    }

    private static void SaveWithKeyboard()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Thread.Sleep(500);
    }

    private static bool HasModernFormattingUi(Window window)
    {
        // The GitHub-hosted Windows runner can have an older Notepad build. Keep the
        // formatting test local/feature-aware instead of failing the whole CI suite.
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
        // Windows Notepad has changed implementation over time.
        // Modern versions commonly expose the editor as Document; older versions as Edit.
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

using System.Diagnostics;
using Allure.Net.Commons;
using Allure.NUnit;
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

[AllureNUnit]
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
        CaptureWindow("00-opened");
    }

    [TearDown]
    public void TearDown()
    {
        var testDocumentClosed = false;

        try
        {
            CaptureWindow("99-final");

            if (_window is not null)
            {
                var editor = FindEditor(_window);
                editor.Focus();
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
                Thread.Sleep(350);

                TryDiscardUnsavedChanges();
                testDocumentClosed = WaitUntilTestFileIsNoLongerOpen(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Cleanup warning: {ex.Message}");
        }
        finally
        {
            if (_tempFilePath is not null && (testDocumentClosed || !IsTestFileOpen()))
            {
                try
                {
                    File.Delete(_tempFilePath);
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Temp-file cleanup warning: {ex.Message}");
                }
            }

            _automation?.Dispose();
        }
    }

    [Test]
    [Category("PortableNotepad")]
    public void CanTypeAndSaveText()
    {
        const string expected = "Hello desktop automation";

        var editor = FindEditor(_window!);
        ReplaceTextWithClipboard(editor, expected);
        CaptureWindow("01-text-entered");

        Assert.That(editor.Text, Does.Contain(expected));

        SaveWithKeyboard();
        CaptureWindow("02-saved");

        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(expected));
    }

    [Test]
    [Category("PortableNotepad")]
    public void CanReplaceExistingTextAndSave()
    {
        const string initialText = "Initial desktop text";
        const string replacementText = "Replaced by FlaUI";

        var editor = FindEditor(_window!);

        ReplaceTextWithClipboard(editor, initialText);
        SaveWithKeyboard();
        CaptureWindow("01-initial-saved");
        Assert.That(File.ReadAllText(_tempFilePath!), Does.Contain(initialText));

        ReplaceTextWithClipboard(editor, replacementText);
        CaptureWindow("02-text-replaced");

        Assert.That(editor.Text, Does.Contain(replacementText));
        Assert.That(editor.Text, Does.Not.Contain(initialText));

        SaveWithKeyboard();
        CaptureWindow("03-replacement-saved");

        var savedText = File.ReadAllText(_tempFilePath!);
        Assert.That(savedText, Does.Contain(replacementText));
        Assert.That(savedText, Does.Not.Contain(initialText));
    }

    [Test]
    [Category("PortableNotepad")]
    public void CanSelectAllCopyReplaceAndSave()
    {
        const string originalText = "Text selected and copied by FlaUI";
        const string replacementText = "Text replaced after copy";

        var editor = FindEditor(_window!);
        ReplaceTextWithClipboard(editor, originalText);
        CaptureWindow("01-original-text-entered");

        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Thread.Sleep(300);
        CaptureWindow("02-text-selected-and-copied");

        var copiedText = GetClipboardTextSta();
        Assert.That(copiedText, Does.Contain(originalText));

        SetClipboardTextSta(replacementText);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Thread.Sleep(300);
        CaptureWindow("03-selection-replaced");

        Assert.That(editor.Text, Does.Contain(replacementText));
        Assert.That(editor.Text, Does.Not.Contain(originalText));

        SaveWithKeyboard();
        CaptureWindow("04-saved");

        var savedText = File.ReadAllText(_tempFilePath!);
        Assert.That(savedText, Does.Contain(replacementText));
        Assert.That(savedText, Does.Not.Contain(originalText));
    }

    private void CaptureWindow(string step)
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            var configuredRoot = Environment.GetEnvironmentVariable("DESKTOP_TEST_ARTIFACTS_DIR");
            var artifactsRoot = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(Directory.GetCurrentDirectory(), "flaui", "TestArtifacts")
                : Path.GetFullPath(configuredRoot);

            var testName = SanitizeFileName(TestContext.CurrentContext.Test.Name);
            var testDirectory = Path.Combine(artifactsRoot, testName);
            Directory.CreateDirectory(testDirectory);

            var fileName = $"{DateTime.UtcNow:HHmmssfff}-{SanitizeFileName(step)}.png";
            var screenshotPath = Path.Combine(testDirectory, fileName);

            _window.CaptureToFile(screenshotPath);
            TestContext.AddTestAttachment(screenshotPath, $"Notepad: {step}");
            AllureApi.AddAttachment($"Notepad - {step}", "image/png", screenshotPath);
            TestContext.Progress.WriteLine($"Screenshot: {screenshotPath}");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Screenshot warning: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void ReplaceTextWithClipboard(FlaUITextBox editor, string text)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
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
            throw new InvalidOperationException(
                "Could not set Windows clipboard for desktop test input.",
                clipboardError);
        }
    }

    private static string GetClipboardTextSta()
    {
        string? clipboardText = null;
        Exception? clipboardError = null;

        var thread = new Thread(() =>
        {
            try
            {
                clipboardText = WinFormsClipboard.ContainsText()
                    ? WinFormsClipboard.GetText()
                    : string.Empty;
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
            throw new InvalidOperationException(
                "Could not read Windows clipboard for desktop test verification.",
                clipboardError);
        }

        return clipboardText ?? string.Empty;
    }

    private static void SaveWithKeyboard()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        Thread.Sleep(600);
    }

    private void TryDiscardUnsavedChanges()
    {
        if (_automation is null || _notepadProcessId is not int processId)
        {
            return;
        }

        var discardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Don't save",
            "Don't Save",
            "Не зберігати",
            "Не сохранять"
        };

        try
        {
            var desktop = _automation.GetDesktop();
            var processWindows = desktop
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Where(element => SafeProcessId(element) == processId)
                .ToArray();

            foreach (var processWindow in processWindows)
            {
                var discardButton = processWindow
                    .FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                    .FirstOrDefault(button => discardNames.Contains(button.Name ?? string.Empty));

                if (discardButton is not null)
                {
                    discardButton.AsButton().Invoke();
                    Thread.Sleep(300);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Save-dialog cleanup warning: {ex.Message}");
        }
    }

    private bool WaitUntilTestFileIsNoLongerOpen(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (!IsTestFileOpen())
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return !IsTestFileOpen();
    }

    private bool IsTestFileOpen()
    {
        if (_automation is null || _tempFilePath is null)
        {
            return false;
        }

        var expectedFileName = Path.GetFileName(_tempFilePath);

        try
        {
            var desktop = _automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

            foreach (var window in windows)
            {
                var processId = SafeProcessId(window);
                if (processId <= 0)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!string.Equals(process.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if ((window.Name ?? string.Empty).Contains(expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (window.FindAllDescendants().Any(element =>
                        (element.Name ?? string.Empty)
                            .Contains(expectedFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Disappeared/inaccessible UI is treated as closed for cleanup purposes.
        }

        return false;
    }

    private static int SafeProcessId(AutomationElement element)
    {
        try
        {
            return element.Properties.ProcessId.Value;
        }
        catch
        {
            return -1;
        }
    }

    private void WriteEnvironmentDiagnostics()
    {
        var executionEnvironment =
            Environment.GetEnvironmentVariable("DESKTOP_TEST_ENVIRONMENT") ?? "local-or-unspecified";

        TestContext.Progress.WriteLine("=== Desktop test environment ===");
        TestContext.Progress.WriteLine($"Execution: {executionEnvironment}");
        TestContext.Progress.WriteLine($"OS: {Environment.OSVersion}");
        TestContext.Progress.WriteLine($"Test file: {_tempFilePath}");

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
                TestContext.Progress.WriteLine($"Notepad PID: {processId}");
                TestContext.Progress.WriteLine(
                    $"Notepad version: {process.MainModule?.FileVersionInfo.FileVersion ?? "unknown"}");
                TestContext.Progress.WriteLine(
                    $"Notepad path: {process.MainModule?.FileName ?? "unknown"}");
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Notepad version/path: unavailable ({ex.Message})");
        }

        TestContext.Progress.WriteLine("================================");
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

                if ((element.Name ?? string.Empty)
                    .Contains(expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return element.AsWindow();
                }

                if (element.FindAllDescendants().Any(descendant =>
                        (descendant.Name ?? string.Empty)
                            .Contains(expectedFileName, StringComparison.OrdinalIgnoreCase)))
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
            throw new InvalidOperationException(
                "Could not find the Notepad editor through UI Automation.");
        }

        return result.Result.AsTextBox();
    }
}

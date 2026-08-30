using System.Diagnostics;
using Allure.Net.Commons;
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

public abstract class NotepadTestBase
{
    private UIA3Automation? _automation;
    private Window? _window;
    private int? _notepadProcessId;
    private int? _windowHandle;
    private bool _windowExistedBeforeTest;
    private string? _tempFilePath;

    protected virtual string InitialText => string.Empty;
    protected virtual string TempFilePrefix => "desktop-automation-";

    protected Window TestWindow =>
        _window ?? throw new InvalidOperationException("Notepad window is not initialized.");

    protected string TestFilePath =>
        _tempFilePath ?? throw new InvalidOperationException("Test file is not initialized.");

    [SetUp]
    public void BaseSetUp()
    {
        _automation = new UIA3Automation();
        var preexistingWindowHandles = GetTopLevelWindowHandles(_automation);

        _tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"{TempFilePrefix}{Guid.NewGuid():N}.txt");
        File.WriteAllText(_tempFilePath, InitialText);

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
        _windowHandle = SafeNativeWindowHandle(_window);
        _windowExistedBeforeTest =
            _windowHandle is int handle && preexistingWindowHandles.Contains(handle);

        WriteEnvironmentDiagnostics();
        CaptureWindow("00-opened");
    }

    [TearDown]
    public void BaseTearDown()
    {
        var testDocumentClosed = false;

        try
        {
            CaptureWindow("99-final");
            BeforeClosingTestDocument();

            if (_window is not null)
            {
                var editor = FindEditor(_window);
                editor.Focus();
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
                Thread.Sleep(250);

                TryDiscardUnsavedChanges();
                testDocumentClosed = WaitUntilTestFileIsNoLongerOpen(TimeSpan.FromSeconds(3));

                if (testDocumentClosed && !_windowExistedBeforeTest)
                {
                    TryCloseTestCreatedWindow();
                }
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
            _automation = null;
            _window = null;
            _notepadProcessId = null;
            _windowHandle = null;
            _windowExistedBeforeTest = false;
            _tempFilePath = null;
        }
    }

    protected virtual void BeforeClosingTestDocument()
    {
    }

    protected FlaUITextBox FindEditor() => FindEditor(TestWindow);

    protected static string ReadEditorText(FlaUITextBox editor) =>
        (editor.Text ?? string.Empty).TrimEnd('\r', '\n');

    protected static void ReplaceTextWithClipboard(FlaUITextBox editor, string text)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        SetClipboardTextSta(text);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        WaitUntil(
            () => ReadEditorText(editor) == text,
            TimeSpan.FromSeconds(3),
            "Notepad editor did not contain the pasted text.");
    }

    protected static void SetClipboardTextSta(string text)
    {
        RunStaClipboardAction(() => WinFormsClipboard.SetText(text), "set");
    }

    protected static string GetClipboardTextSta()
    {
        string clipboardText = string.Empty;
        RunStaClipboardAction(
            () => clipboardText = WinFormsClipboard.ContainsText()
                ? WinFormsClipboard.GetText()
                : string.Empty,
            "read");
        return clipboardText;
    }

    protected void SaveAndWaitForText(string expected)
    {
        var editor = FindEditor();
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);

        WaitUntil(
            () =>
            {
                try
                {
                    return File.ReadAllText(TestFilePath) == expected;
                }
                catch (IOException)
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(5),
            $"Saved file did not become exactly '{expected}'.");
    }

    protected void CaptureWindow(string step)
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

    protected static void WaitUntil(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage,
        TimeSpan? interval = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var pollInterval = interval ?? TimeSpan.FromMilliseconds(100);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch
            {
                // Transient UI/file-system state; retry until timeout.
            }

            Thread.Sleep(pollInterval);
        }

        if (!condition())
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private static void RunStaClipboardAction(Action action, string operation)
    {
        Exception? clipboardError = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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
                $"Could not {operation} the Windows clipboard for desktop automation.",
                clipboardError);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
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
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Save-dialog cleanup warning: {ex.Message}");
        }
    }

    private void TryCloseTestCreatedWindow()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            _window.Close();

            if (_windowHandle is int handle)
            {
                var closed = Retry.WhileTrue(
                    () => WindowHandleExists(handle),
                    timeout: TimeSpan.FromSeconds(3),
                    interval: TimeSpan.FromMilliseconds(100),
                    ignoreException: true);

                if (!closed.Success)
                {
                    TestContext.Progress.WriteLine(
                        $"Cleanup warning: test-created Notepad window {handle} did not close.");
                }
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Window cleanup warning: {ex.Message}");
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
            Thread.Sleep(100);
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

                if (ContainsFileName(window, expectedFileName))
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

    private static HashSet<int> GetTopLevelWindowHandles(UIA3Automation automation)
    {
        try
        {
            return automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Select(SafeNativeWindowHandle)
                .Where(handle => handle is > 0)
                .Select(handle => handle!.Value)
                .ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private bool WindowHandleExists(int handle)
    {
        if (_automation is null)
        {
            return false;
        }

        try
        {
            return _automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Any(element => SafeNativeWindowHandle(element) == handle);
        }
        catch
        {
            return false;
        }
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

    private static int? SafeNativeWindowHandle(AutomationElement element)
    {
        try
        {
            return element.Properties.NativeWindowHandle.Value;
        }
        catch
        {
            return null;
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
        TestContext.Progress.WriteLine($"Window existed before test: {_windowExistedBeforeTest}");

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

                if (ContainsFileName(element, expectedFileName))
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

    private static bool ContainsFileName(AutomationElement element, string expectedFileName)
    {
        if ((element.Name ?? string.Empty).Contains(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return element.FindAllDescendants().Any(descendant =>
            (descendant.Name ?? string.Empty).Contains(expectedFileName, StringComparison.OrdinalIgnoreCase));
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
                var processId = SafeProcessId(element);
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

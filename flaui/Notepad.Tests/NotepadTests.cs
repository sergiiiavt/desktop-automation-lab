using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
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

    [SetUp]
    public void SetUp()
    {
        _automation = new UIA3Automation();

        // Windows 11 Notepad may be launched through a short-lived stub process.
        // Do not trust the PID returned by Process.Start(). Instead, launch the app
        // and discover its real top-level window directly in the UI Automation tree.
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            UseShellExecute = true
        })?.Dispose();

        var windowResult = Retry.WhileNull(
            () => FindNotepadWindow(_automation),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(250),
            ignoreException: true);

        if (!windowResult.Success || windowResult.Result is null)
        {
            throw new InvalidOperationException(
                "Could not find a Notepad top-level window through UI Automation. " +
                "Close any existing Notepad windows and run the test again.\n\n" +
                BuildDesktopDiagnostic(_automation));
        }

        _window = windowResult.Result;
        _notepadProcessId = _window.Properties.ProcessId.Value;
    }

    [TearDown]
    public void TearDown()
    {
        // Kill the real process discovered from the UIA window so an unsaved-document
        // dialog cannot block teardown. The launcher process may already be gone.
        try
        {
            if (_notepadProcessId is int processId)
            {
                var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                process.Dispose();
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
        }
    }

    [Test]
    public void CanTypeAndReadText()
    {
        const string expected = "Hello desktop automation";

        var editor = FindEditor(_window!);
        editor.Focus();
        editor.Text = expected;

        Thread.Sleep(300);

        Assert.That(editor.Text, Does.Contain(expected));
    }

    private static Window? FindNotepadWindow(UIA3Automation automation)
    {
        var desktop = automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        foreach (var element in windows)
        {
            try
            {
                var processId = element.Properties.ProcessId.Value;
                using var process = Process.GetProcessById(processId);

                if (string.Equals(process.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase))
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

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

namespace Notepad.Tests;

[AllureNUnit]
[TestFixture]
[NonParallelizable]
public class NotepadZoomTests
{
    private const string VisibleText = "Visible FlaUI zoom test - this text should become noticeably larger";

    private UIA3Automation? _automation;
    private Window? _window;
    private string? _tempFilePath;

    [SetUp]
    public void SetUp()
    {
        _automation = new UIA3Automation();
        _tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"desktop-automation-zoom-{Guid.NewGuid():N}.txt");

        File.WriteAllText(_tempFilePath, VisibleText);

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
                $"Could not find the Notepad window for '{expectedFileName}'.");
        }

        _window = windowResult.Result;
        CaptureWindow("00-opened");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            CaptureWindow("99-final");

            if (_window is not null)
            {
                var editor = FindEditor(_window);
                editor.Focus();

                // Always return Notepad zoom to its default before closing so this
                // test does not affect later tests or the developer's local Notepad.
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
                Thread.Sleep(200);

                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
                Thread.Sleep(350);
            }
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Zoom-test cleanup warning: {ex.Message}");
        }
        finally
        {
            if (_tempFilePath is not null)
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
    public void CanZoomTextInAndReset()
    {
        var editor = FindEditor(_window!);
        Assert.That(editor.Text, Does.Contain(VisibleText));

        editor.Focus();

        // Ctrl+0 is supported by both classic and modern Notepad and gives us a
        // deterministic starting point for a visual zoom test.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        var baselineZoom = WaitForZoomPercentage(_window!, value => value == 100);
        CaptureWindow("01-zoom-100-percent");

        // VK_ADD = numpad '+'. Sending the virtual key works even on a keyboard
        // without a physical numpad and avoids keyboard-layout differences.
        var numpadPlus = (VirtualKeyShort)0x6B;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, numpadPlus);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, numpadPlus);

        var enlargedZoom = WaitForZoomPercentage(_window!, value => value > baselineZoom);
        CaptureWindow($"02-zoomed-{enlargedZoom}-percent");

        Assert.That(enlargedZoom, Is.GreaterThan(baselineZoom));
        Assert.That(editor.Text, Does.Contain(VisibleText));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        var resetZoom = WaitForZoomPercentage(_window!, value => value == baselineZoom);
        CaptureWindow("03-zoom-reset-100-percent");

        Assert.That(resetZoom, Is.EqualTo(100));
        Assert.That(editor.Text, Does.Contain(VisibleText));
    }

    private static int WaitForZoomPercentage(Window window, Func<int, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        int? lastSeen = null;

        while (DateTime.UtcNow < deadline)
        {
            var current = FindZoomPercentage(window);
            if (current.HasValue)
            {
                lastSeen = current;
                if (condition(current.Value))
                {
                    return current.Value;
                }
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            $"Notepad zoom indicator did not reach the expected value. Last seen: " +
            $"{(lastSeen.HasValue ? $"{lastSeen.Value}%" : "<not exposed through UI Automation>")}.\n" +
            BuildZoomDiagnostic(window));
    }

    private static int? FindZoomPercentage(Window window)
    {
        try
        {
            foreach (var element in window.FindAllDescendants())
            {
                string name;

                try
                {
                    name = (element.Name ?? string.Empty).Trim();
                }
                catch
                {
                    continue;
                }

                if (name.Length < 2 || !name.EndsWith('%'))
                {
                    continue;
                }

                if (int.TryParse(name[..^1], out var percentage))
                {
                    return percentage;
                }
            }
        }
        catch
        {
            // The retry loop will produce a useful diagnostic if the provider
            // temporarily refuses enumeration.
        }

        return null;
    }

    private static string BuildZoomDiagnostic(Window window)
    {
        try
        {
            var names = window.FindAllDescendants()
                .Select(element =>
                {
                    try
                    {
                        return element.Name ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                })
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => name.Contains('%') || name.Contains("Zoom", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Take(30)
                .ToArray();

            return names.Length == 0
                ? "No zoom/status text was exposed through UI Automation."
                : "Zoom/status UIA names: " + string.Join(" | ", names);
        }
        catch (Exception ex)
        {
            return $"Could not build zoom diagnostic: {ex.Message}";
        }
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

            var testDirectory = Path.Combine(artifactsRoot, TestContext.CurrentContext.Test.Name);
            Directory.CreateDirectory(testDirectory);

            var screenshotPath = Path.Combine(
                testDirectory,
                $"{DateTime.UtcNow:HHmmssfff}-{step}.png");

            _window.CaptureToFile(screenshotPath);
            TestContext.AddTestAttachment(screenshotPath, $"Notepad zoom: {step}");
            AllureApi.AddAttachment($"Notepad zoom - {step}", "image/png", screenshotPath);
            TestContext.Progress.WriteLine($"Screenshot: {screenshotPath}");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Screenshot warning: {ex.Message}");
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

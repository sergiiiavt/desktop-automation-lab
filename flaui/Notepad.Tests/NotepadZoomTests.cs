using Allure.NUnit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Notepad.Tests;

[AllureNUnit]
[TestFixture]
[NonParallelizable]
public class NotepadZoomTests : NotepadTestBase
{
    private const string VisibleText =
        "Visible FlaUI zoom test - this text should become noticeably larger";

    protected override string InitialText => VisibleText;
    protected override string TempFilePrefix => "desktop-automation-zoom-";

    [Test]
    [Category("PortableNotepad")]
    public void CanZoomTextInAndReset()
    {
        var editor = FindEditor();
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));

        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        var baselineZoom = WaitForZoomPercentage(TestWindow, value => value == 100);
        CaptureWindow("01-zoom-100-percent");

        var numpadPlus = (VirtualKeyShort)0x6B;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, numpadPlus);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, numpadPlus);

        var enlargedZoom = WaitForZoomPercentage(TestWindow, value => value > baselineZoom);
        CaptureWindow($"02-zoomed-{enlargedZoom}-percent");

        Assert.That(enlargedZoom, Is.GreaterThan(baselineZoom));
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        var resetZoom = WaitForZoomPercentage(TestWindow, value => value == 100);
        CaptureWindow("03-zoom-reset-100-percent");

        Assert.That(resetZoom, Is.EqualTo(100));
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));
    }

    protected override void BeforeClosingTestDocument()
    {
        try
        {
            var editor = FindEditor();
            editor.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
            WaitForZoomPercentage(TestWindow, value => value == 100);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Zoom reset warning: {ex.Message}");
        }
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
            // Retry loop emits the useful diagnostic on timeout.
        }

        return null;
    }

    private static string BuildZoomDiagnostic(Window window)
    {
        try
        {
            var names = window.FindAllDescendants()
                .Select(element => SafeName(element))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => name.Contains('%') ||
                               name.Contains("Zoom", StringComparison.OrdinalIgnoreCase))
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

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

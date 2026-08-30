using Allure.Net.Commons;
using Allure.NUnit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;
using FlaUITextBox = FlaUI.Core.AutomationElements.TextBox;

namespace Notepad.Tests;

[AllureNUnit]
[TestFixture]
[NonParallelizable]
public class NotepadZoomTests : NotepadTestBase
{
    private const string TestCaseId = "TC0004";
    private const string VisibleText =
        "[TC0004] Visible FlaUI zoom test - this text should become noticeably larger";

    protected override string InitialText => VisibleText;
    protected override string TempFilePrefix => "desktop-automation-zoom-";

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", TestCaseId)]
    public void TC0004_CanZoomTextInAndReset()
    {
        AllureApi.SetTestName("TC0004 | Can zoom text in and reset");
        AllureApi.AddLabel("suite", "FlaUI - Notepad");
        AllureApi.AddLabel("testCaseId", TestCaseId);
        AllureApi.AddTags(TestCaseId);
        TestContext.Progress.WriteLine("Test case: TC0004 | Can zoom text in and reset");

        var editor = FindEditor();
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));

        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(150);

        var baseline = ReadZoomState(TestWindow, editor);
        AssertZoomStateIsObservable(baseline, "baseline after Ctrl+0");
        LogZoomState("Baseline", baseline);
        CaptureWindow("01-zoom-baseline");

        ZoomIn();
        ZoomIn();

        var enlarged = WaitForZoomChange(TestWindow, editor, baseline);
        LogZoomState("Enlarged", enlarged);
        CaptureWindow("02-zoomed-in");

        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        Wait.UntilInputIsProcessed();

        var reset = WaitForZoomReset(TestWindow, editor, baseline);
        LogZoomState("Reset", reset);
        CaptureWindow("03-zoom-reset");

        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));
    }

    protected override void BeforeClosingTestDocument()
    {
        try
        {
            var editor = FindEditor();
            editor.Focus();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
            Wait.UntilInputIsProcessed();
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Zoom reset warning: {ex.Message}");
        }
    }

    private static void ZoomIn()
    {
        // VK_OEM_PLUS is the main keyboard +/= key. It works with modern Windows 11
        // Notepad, unlike VK_ADD (numpad +), which is not handled consistently.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_PLUS);
        Wait.UntilInputIsProcessed();
    }

    private static ZoomState WaitForZoomChange(
        Window window,
        FlaUITextBox editor,
        ZoomState baseline)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var last = ReadZoomState(window, editor);

        while (DateTime.UtcNow < deadline)
        {
            last = ReadZoomState(window, editor);

            if (IsZoomedIn(baseline, last))
            {
                return last;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "Notepad did not expose evidence that zoom changed after Ctrl++.\n" +
            $"Baseline: {DescribeZoomState(baseline)}\n" +
            $"Last seen: {DescribeZoomState(last)}\n" +
            BuildZoomDiagnostic(window));
    }

    private static ZoomState WaitForZoomReset(
        Window window,
        FlaUITextBox editor,
        ZoomState baseline)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var last = ReadZoomState(window, editor);

        while (DateTime.UtcNow < deadline)
        {
            last = ReadZoomState(window, editor);

            if (IsResetToBaseline(baseline, last))
            {
                return last;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "Notepad zoom did not return to the baseline after Ctrl+0.\n" +
            $"Baseline: {DescribeZoomState(baseline)}\n" +
            $"Last seen: {DescribeZoomState(last)}\n" +
            BuildZoomDiagnostic(window));
    }

    private static bool IsZoomedIn(ZoomState baseline, ZoomState current)
    {
        if (baseline.Percentage.HasValue &&
            current.Percentage.HasValue &&
            current.Percentage.Value > baseline.Percentage.Value)
        {
            return true;
        }

        if (!baseline.Percentage.HasValue &&
            current.Percentage.HasValue &&
            current.Percentage.Value > 100)
        {
            return true;
        }

        return baseline.RenderedText.HasValue &&
               current.RenderedText.HasValue &&
               IsMeaningfullyLarger(baseline.RenderedText.Value, current.RenderedText.Value);
    }

    private static bool IsResetToBaseline(ZoomState baseline, ZoomState current)
    {
        if (current.Percentage == 100)
        {
            return true;
        }

        return baseline.RenderedText.HasValue &&
               current.RenderedText.HasValue &&
               IsApproximatelySameSize(baseline.RenderedText.Value, current.RenderedText.Value);
    }

    private static bool IsMeaningfullyLarger(RenderedTextSize baseline, RenderedTextSize current)
    {
        const double scaleThreshold = 1.08;
        return current.Width > baseline.Width * scaleThreshold ||
               current.Height > baseline.Height * scaleThreshold;
    }

    private static bool IsApproximatelySameSize(RenderedTextSize baseline, RenderedTextSize current)
    {
        var widthTolerance = Math.Max(4, baseline.Width * 0.05);
        var heightTolerance = Math.Max(3, baseline.Height * 0.05);

        return Math.Abs(current.Width - baseline.Width) <= widthTolerance &&
               Math.Abs(current.Height - baseline.Height) <= heightTolerance;
    }

    private static ZoomState ReadZoomState(Window window, FlaUITextBox editor) =>
        new(FindZoomPercentage(window), FindRenderedTextSize(editor));

    private static RenderedTextSize? FindRenderedTextSize(FlaUITextBox editor)
    {
        try
        {
            if (!editor.Patterns.Text.TryGetPattern(out var textPattern))
            {
                return null;
            }

            var rectangles = textPattern.DocumentRange
                .GetBoundingRectangles()
                .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0)
                .ToArray();

            if (rectangles.Length == 0)
            {
                return null;
            }

            var left = rectangles.Min(rectangle => rectangle.Left);
            var top = rectangles.Min(rectangle => rectangle.Top);
            var right = rectangles.Max(rectangle => rectangle.Right);
            var bottom = rectangles.Max(rectangle => rectangle.Bottom);

            return new RenderedTextSize(right - left, bottom - top);
        }
        catch
        {
            return null;
        }
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
            // The visual TextPattern measurement below is the portable fallback.
        }

        return null;
    }

    private static void AssertZoomStateIsObservable(ZoomState state, string stage)
    {
        if (!state.Percentage.HasValue && !state.RenderedText.HasValue)
        {
            throw new InvalidOperationException(
                $"Notepad zoom state is not observable at {stage}: neither a percentage " +
                "indicator nor TextPattern bounding rectangles are exposed through UI Automation.");
        }
    }

    private static void LogZoomState(string label, ZoomState state) =>
        TestContext.Progress.WriteLine($"{label} zoom state: {DescribeZoomState(state)}");

    private static string DescribeZoomState(ZoomState state)
    {
        var percentage = state.Percentage.HasValue
            ? $"{state.Percentage.Value}%"
            : "<percentage not exposed>";

        var renderedText = state.RenderedText.HasValue
            ? $"{state.RenderedText.Value.Width}x{state.RenderedText.Value.Height}px"
            : "<text bounds not exposed>";

        return $"indicator={percentage}; renderedText={renderedText}";
    }

    private static string BuildZoomDiagnostic(Window window)
    {
        try
        {
            var names = window.FindAllDescendants()
                .Select(SafeName)
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

    private readonly record struct ZoomState(
        int? Percentage,
        RenderedTextSize? RenderedText);

    private readonly record struct RenderedTextSize(int Width, int Height);
}

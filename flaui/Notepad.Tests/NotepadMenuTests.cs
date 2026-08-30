using Allure.Net.Commons;
using Allure.NUnit;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Notepad.Tests;

[AllureNUnit]
[TestFixture]
[NonParallelizable]
public class NotepadMenuTests : NotepadTestBase
{
    private const string TestCaseId = "TC0006";
    private const string VisibleText =
        "[TC0006] FlaUI menu test - watch View > Zoom > Zoom in being clicked";

    protected override string InitialText => VisibleText;
    protected override string TempFilePrefix => "desktop-automation-menu-flaui-";

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", TestCaseId)]
    public void TC0006_CanZoomInThroughVisibleMenuClicks()
    {
        AllureApi.SetTestName("TC0006 | Can zoom in through visible menu clicks");
        AllureApi.AddLabel("suite", "FlaUI - Notepad");
        AllureApi.AddLabel("testCaseId", TestCaseId);
        AllureApi.AddTags(TestCaseId, "menu", "mouse");
        TestContext.Progress.WriteLine(
            "Test case: TC0006 | Physically click View > Zoom > Zoom in");

        var editor = FindEditor();
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));

        ResetZoom(editor);
        var baseline = ReadRenderedTextSize(editor);
        Assert.That(baseline, Is.Not.Null,
            "Notepad did not expose TextPattern bounds needed to verify the visible zoom change.");
        CaptureWindow("01-before-menu-clicks");

        ClickMenuItem("View");
        Thread.Sleep(450);
        CaptureWindow("02-view-menu-open");

        ClickMenuItem("Zoom");
        Thread.Sleep(450);
        CaptureWindow("03-zoom-submenu-open");

        ClickMenuItem("Zoom in");
        Wait.UntilInputIsProcessed();

        var enlarged = WaitForLargerText(editor, baseline!.Value);
        CaptureWindow("04-after-zoom-in-menu-click");

        Assert.That(enlarged.Width > baseline.Value.Width * 1.05 ||
                    enlarged.Height > baseline.Value.Height * 1.05,
            Is.True,
            $"Rendered text did not become larger. Before={baseline.Value}; after={enlarged}");
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));
    }

    protected override void BeforeClosingTestDocument()
    {
        try
        {
            ResetZoom(FindEditor());
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Menu-test zoom reset warning: {ex.Message}");
        }
    }

    private void ClickMenuItem(string name)
    {
        var item = WaitForMenuItem(name);
        var point = item.GetClickablePoint();
        TestContext.Progress.WriteLine(
            $"Physical menu click: '{name}' at ({point.X:0}, {point.Y:0})");
        Mouse.Click(point);
        Wait.UntilInputIsProcessed();
    }

    private AutomationElement WaitForMenuItem(string name)
    {
        AutomationElement? found = null;
        WaitUntil(
            () =>
            {
                found = FindMenuItem(name);
                return found is not null;
            },
            TimeSpan.FromSeconds(4),
            $"Visible Notepad menu item '{name}' was not found through UI Automation.");
        return found!;
    }

    private AutomationElement? FindMenuItem(string name)
    {
        try
        {
            return TestWindow.FindAllDescendants()
                .FirstOrDefault(element =>
                    SafeName(element).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    element.ControlType == ControlType.MenuItem);
        }
        catch
        {
            return null;
        }
    }

    private static void ResetZoom(FlaUI.Core.AutomationElements.TextBox editor)
    {
        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(150);
    }

    private static RenderedTextSize WaitForLargerText(
        FlaUI.Core.AutomationElements.TextBox editor,
        RenderedTextSize baseline)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        RenderedTextSize? last = null;

        while (DateTime.UtcNow < deadline)
        {
            last = ReadRenderedTextSize(editor);
            if (last.HasValue &&
                (last.Value.Width > baseline.Width * 1.05 ||
                 last.Value.Height > baseline.Height * 1.05))
            {
                return last.Value;
            }
            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            $"Zoom in menu command was clicked but rendered text did not grow. " +
            $"Before={baseline}; last={last?.ToString() ?? "<not exposed>"}");
    }

    private static RenderedTextSize? ReadRenderedTextSize(
        FlaUI.Core.AutomationElements.TextBox editor)
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

    private readonly record struct RenderedTextSize(int Width, int Height);
}

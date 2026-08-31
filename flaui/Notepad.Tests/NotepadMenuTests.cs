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
    private const string TestCaseId = "TC0005";
    private const string VisibleText =
        "[TC0005] FlaUI locale-independent menu test";
    private const string PasteText =
        "[TC0005] pasted through the visible menu";

    protected override string InitialText => VisibleText;
    protected override string TempFilePrefix => "desktop-automation-menu-flaui-";

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", TestCaseId)]
    public void TC0005_CanPasteThroughLocaleIndependentVisibleMenu()
    {
        AllureApi.SetTestName(
            "TC0005 | Can paste through a locale-independent visible menu click");
        AllureApi.AddLabel("suite", "FlaUI - Notepad");
        AllureApi.AddLabel("testCaseId", TestCaseId);
        AllureApi.AddTags(
            TestCaseId,
            "menu",
            "mouse",
            "portable",
            "locale-independent");
        TestContext.Progress.WriteLine(
            "Test case: TC0005 | Physically open the second top menu and click the Ctrl+V command");

        var editor = FindEditor();
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));
        CaptureWindow("01-before-menu-click");

        SetClipboardTextSta(PasteText);
        editor.Focus();
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);

        OpenSecondTopMenu(editor);
        CaptureWindow("02-localized-edit-menu-open");
        ClickVisibleCommandByAccelerator("Ctrl+V");

        var expected = VisibleText + Environment.NewLine + PasteText;
        WaitUntil(
            () => NormalizeNewlines(ReadEditorText(editor)) == NormalizeNewlines(expected),
            TimeSpan.FromSeconds(3),
            "The visible Ctrl+V menu command was clicked, but the expected text was not pasted.");
        CaptureWindow("03-pasted-through-visible-menu");

        Assert.That(
            NormalizeNewlines(ReadEditorText(editor)),
            Is.EqualTo(NormalizeNewlines(expected)));
    }

    private void OpenSecondTopMenu(FlaUI.Core.AutomationElements.TextBox editor)
    {
        var windowRect = TestWindow.BoundingRectangle;
        var editorTop = editor.BoundingRectangle.Top;
        var maxLeft = windowRect.Left + Math.Min(430, (int)(windowRect.Width * 0.40));

        var candidates = TestWindow.FindAllDescendants()
            .Where(element =>
                element.ControlType is ControlType.Button or ControlType.MenuItem)
            .Where(element => IsVisibleAndEnabled(element))
            .Where(element =>
            {
                var rectangle = element.BoundingRectangle;
                if (rectangle.Width <= 0 || rectangle.Height <= 0)
                {
                    return false;
                }

                var centerY = rectangle.Top + rectangle.Height / 2;
                return centerY > windowRect.Top + 28 &&
                       centerY < editorTop &&
                       rectangle.Left < maxLeft;
            })
            .OrderBy(element => element.BoundingRectangle.Left)
            .ThenBy(element => element.BoundingRectangle.Top)
            .ToArray();

        if (candidates.Length < 2)
        {
            var description = string.Join(
                " | ",
                candidates.Select(element =>
                    $"{element.ControlType}:{SafeName(element)}@{element.BoundingRectangle}"));
            throw new InvalidOperationException(
                "Could not locate the first two top-level Notepad menu controls by geometry. " +
                $"Candidates: {description}");
        }

        var secondMenu = candidates[1];
        var point = secondMenu.GetClickablePoint();
        TestContext.Progress.WriteLine(
            $"Physical top-menu click: '{SafeName(secondMenu)}' at ({point.X:0}, {point.Y:0})");
        Mouse.Click(point);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
    }

    private void ClickVisibleCommandByAccelerator(string accelerator)
    {
        AutomationElement? found = null;
        var normalizedExpected = NormalizeAccelerator(accelerator);
        var diagnostics = Array.Empty<string>();

        WaitUntil(
            () =>
            {
                var desktop = TestWindow.Automation.GetDesktop();
                var elements = desktop.FindAllDescendants();

                diagnostics = elements
                    .Where(IsVisibleAndEnabled)
                    .Select(element => new
                    {
                        Element = element,
                        Accelerator = SafeAcceleratorKey(element)
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Accelerator))
                    .Select(entry =>
                        $"{entry.Element.ControlType}:{SafeName(entry.Element)}={entry.Accelerator}")
                    .TakeLast(30)
                    .ToArray();

                found = elements.FirstOrDefault(element =>
                    IsVisibleAndEnabled(element) &&
                    NormalizeAccelerator(SafeAcceleratorKey(element)) == normalizedExpected);

                return found is not null;
            },
            TimeSpan.FromSeconds(4),
            $"No visible menu command exposed accelerator '{accelerator}'. " +
            $"Accelerators seen: {string.Join(" | ", diagnostics)}");

        var point = found!.GetClickablePoint();
        TestContext.Progress.WriteLine(
            $"Physical command click by accelerator '{accelerator}': " +
            $"'{SafeName(found)}' at ({point.X:0}, {point.Y:0})");
        Mouse.Click(point);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(300);
    }

    private static bool IsVisibleAndEnabled(AutomationElement element)
    {
        try
        {
            return !element.IsOffscreen && element.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeAcceleratorKey(AutomationElement element)
    {
        try
        {
            return element.Properties.AcceleratorKey.ValueOrDefault ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeAccelerator(string value) =>
        value.Replace(" ", string.Empty).ToUpperInvariant();

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');

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

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
        "[TC0006] FlaUI portable menu test";

    protected override string InitialText => VisibleText;
    protected override string TempFilePrefix => "desktop-automation-menu-flaui-";

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", TestCaseId)]
    public void TC0006_CanCopyAndPasteThroughVisibleMenuClicks()
    {
        AllureApi.SetTestName("TC0006 | Can copy and paste through visible menu clicks");
        AllureApi.AddLabel("suite", "FlaUI - Notepad");
        AllureApi.AddLabel("testCaseId", TestCaseId);
        AllureApi.AddTags(TestCaseId, "menu", "mouse", "portable");
        TestContext.Progress.WriteLine(
            "Test case: TC0006 | Physically click Edit > Select all > Copy, then Edit > Paste");

        var editor = FindEditor();
        Assert.That(ReadEditorText(editor), Is.EqualTo(VisibleText));
        CaptureWindow("01-before-menu-clicks");

        ClickMenuCommand("Edit", "Select all", "02-edit-menu-open-before-select-all");
        Thread.Sleep(200);

        ClickMenuCommand("Edit", "Copy", "03-edit-menu-open-before-copy");
        WaitUntil(
            () => GetClipboardTextSta() == VisibleText,
            TimeSpan.FromSeconds(3),
            $"Edit > Copy did not place {VisibleText!r} on the Windows clipboard.");
        Assert.That(GetClipboardTextSta(), Is.EqualTo(VisibleText));
        CaptureWindow("04-copied-through-menu");

        editor.Focus();
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();

        ClickMenuCommand("Edit", "Paste", "05-edit-menu-open-before-paste");

        var expected = VisibleText + Environment.NewLine + VisibleText;
        WaitUntil(
            () => NormalizeNewlines(ReadEditorText(editor)) == NormalizeNewlines(expected),
            TimeSpan.FromSeconds(3),
            "Edit > Paste did not append the copied text on a new line.");
        CaptureWindow("06-pasted-through-menu");

        Assert.That(
            NormalizeNewlines(ReadEditorText(editor)),
            Is.EqualTo(NormalizeNewlines(expected)));
    }

    private void ClickMenuCommand(string menuName, string commandName, string openStep)
    {
        ClickMenuItem(menuName);
        Thread.Sleep(350);
        CaptureWindow(openStep);
        ClickMenuItem(commandName);
        Thread.Sleep(250);
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

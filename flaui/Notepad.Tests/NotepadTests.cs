using Allure.Net.Commons;
using Allure.NUnit;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Notepad.Tests;

[AllureNUnit]
[TestFixture]
[NonParallelizable]
public class NotepadTests : NotepadTestBase
{
    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", "TC0001")]
    public void TC0001_CanTypeAndSaveText()
    {
        const string testCaseId = "TC0001";
        const string expected = "[TC0001] Hello desktop automation";
        RegisterTestCase(testCaseId, "Can type and save text");

        var editor = FindEditor();
        ReplaceTextWithClipboard(editor, expected);
        CaptureWindow("01-text-entered");

        Assert.That(ReadEditorText(editor), Is.EqualTo(expected));

        SaveAndWaitForText(expected);
        CaptureWindow("02-saved");

        Assert.That(File.ReadAllText(TestFilePath), Is.EqualTo(expected));
    }

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", "TC0002")]
    public void TC0002_CanReplaceExistingTextAndSave()
    {
        const string testCaseId = "TC0002";
        const string initialText = "[TC0002] Initial desktop text";
        const string replacementText = "[TC0002] Replaced by FlaUI";
        RegisterTestCase(testCaseId, "Can replace existing text and save");

        var editor = FindEditor();

        ReplaceTextWithClipboard(editor, initialText);
        SaveAndWaitForText(initialText);
        CaptureWindow("01-initial-saved");
        Assert.That(File.ReadAllText(TestFilePath), Is.EqualTo(initialText));

        ReplaceTextWithClipboard(editor, replacementText);
        CaptureWindow("02-text-replaced");
        Assert.That(ReadEditorText(editor), Is.EqualTo(replacementText));

        SaveAndWaitForText(replacementText);
        CaptureWindow("03-replacement-saved");
        Assert.That(File.ReadAllText(TestFilePath), Is.EqualTo(replacementText));
    }

    [Test]
    [Category("PortableNotepad")]
    [Property("TestCaseId", "TC0003")]
    public void TC0003_CanCopyAndPasteTextAsMultipleLines()
    {
        const string testCaseId = "TC0003";
        const string originalText = "[TC0003] Clipboard text copied by FlaUI";
        var expectedMultiline = string.Join(
            Environment.NewLine,
            originalText,
            originalText,
            originalText);

        RegisterTestCase(testCaseId, "Can copy and paste text as multiple lines");

        var editor = FindEditor();
        ReplaceTextWithClipboard(editor, originalText);
        CaptureWindow("01-original-text-entered");

        editor.Focus();
        SendCtrlShortcut(VirtualKeyShort.KEY_A);
        SendCtrlShortcut(VirtualKeyShort.KEY_C);
        WaitUntil(
            () => GetClipboardTextSta() == originalText,
            TimeSpan.FromSeconds(2),
            "Copied text did not reach the Windows clipboard.");
        CaptureWindow("02-text-selected-and-copied");
        Assert.That(GetClipboardTextSta(), Is.EqualTo(originalText));

        SendCtrlShortcut(VirtualKeyShort.KEY_V);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        SendCtrlShortcut(VirtualKeyShort.KEY_V);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        SendCtrlShortcut(VirtualKeyShort.KEY_V);

        WaitUntil(
            () => MultilineEquals(ReadEditorText(editor), expectedMultiline),
            TimeSpan.FromSeconds(3),
            $"Copied text was not pasted as the expected three-line document. " +
            $"Actual UIA text: {DescribeForLog(ReadEditorText(editor))}");
        CaptureWindow("03-multiline-paste-complete");
        Assert.That(
            NormalizeNewlines(ReadEditorText(editor)),
            Is.EqualTo(NormalizeNewlines(expectedMultiline)));

        SaveAndWaitForText(expectedMultiline);
        CaptureWindow("04-multiline-saved");
        Assert.That(File.ReadAllText(TestFilePath), Is.EqualTo(expectedMultiline));
    }

    private static bool MultilineEquals(string actual, string expected) =>
        NormalizeNewlines(actual) == NormalizeNewlines(expected);

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string DescribeForLog(string value) =>
        value.Replace("\r", "\\r").Replace("\n", "\\n");

    private static void SendCtrlShortcut(VirtualKeyShort key)
    {
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
        {
            Wait.UntilInputIsProcessed();
            Thread.Sleep(60);
            Keyboard.Type(key);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(60);
        }
        Wait.UntilInputIsProcessed();
    }

    private static void RegisterTestCase(string testCaseId, string title)
    {
        AllureApi.SetTestName($"{testCaseId} | {title}");
        AllureApi.AddLabel("suite", "FlaUI - Notepad");
        AllureApi.AddLabel("testCaseId", testCaseId);
        AllureApi.AddTags(testCaseId);
        TestContext.Progress.WriteLine($"Test case: {testCaseId} | {title}");
    }
}

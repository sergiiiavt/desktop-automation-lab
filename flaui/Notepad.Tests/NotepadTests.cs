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
    public void TC0003_CanSelectAllCopyReplaceAndSave()
    {
        const string testCaseId = "TC0003";
        const string originalText = "[TC0003] Text selected and copied by FlaUI";
        const string replacementText = "[TC0003] Text replaced after copy";
        RegisterTestCase(testCaseId, "Can select all, copy, replace and save");

        var editor = FindEditor();
        ReplaceTextWithClipboard(editor, originalText);
        CaptureWindow("01-original-text-entered");

        editor.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        WaitUntil(
            () => GetClipboardTextSta() == originalText,
            TimeSpan.FromSeconds(2),
            "Copied text did not reach the Windows clipboard.");
        CaptureWindow("02-text-selected-and-copied");

        Assert.That(GetClipboardTextSta(), Is.EqualTo(originalText));

        SetClipboardTextSta(replacementText);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        WaitUntil(
            () => ReadEditorText(editor) == replacementText,
            TimeSpan.FromSeconds(3),
            "Selected text was not replaced after paste.");
        CaptureWindow("03-selection-replaced");

        Assert.That(ReadEditorText(editor), Is.EqualTo(replacementText));

        SaveAndWaitForText(replacementText);
        CaptureWindow("04-saved");
        Assert.That(File.ReadAllText(TestFilePath), Is.EqualTo(replacementText));
    }

    private static void RegisterTestCase(string testCaseId, string title)
    {
        AllureApi.SetTestName($"{testCaseId} | {title}");
        AllureApi.AddLabel("testCaseId", testCaseId);
        AllureApi.AddTags(testCaseId);
        TestContext.Progress.WriteLine($"Test case: {testCaseId} | {title}");
    }
}

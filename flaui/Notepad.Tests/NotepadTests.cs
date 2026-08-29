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
    public void CanTypeAndSaveText()
    {
        const string expected = "Hello desktop automation";

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
    public void CanReplaceExistingTextAndSave()
    {
        const string initialText = "Initial desktop text";
        const string replacementText = "Replaced by FlaUI";

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
    public void CanSelectAllCopyReplaceAndSave()
    {
        const string originalText = "Text selected and copied by FlaUI";
        const string replacementText = "Text replaced after copy";

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
}

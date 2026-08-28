using FlaUI.Core;
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
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _window;

    [SetUp]
    public void SetUp()
    {
        _app = Application.Launch("notepad.exe");
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));

        Assert.That(_window, Is.Not.Null, "Notepad main window was not found.");
    }

    [TearDown]
    public void TearDown()
    {
        // Killing the process avoids the Save dialog after the test modifies the document.
        try
        {
            if (_app is { HasExited: false })
            {
                _app.Kill();
            }
        }
        finally
        {
            _automation?.Dispose();
            _app?.Dispose();
        }
    }

    [Test]
    public void CanTypeAndReadText()
    {
        const string expected = "Hello desktop automation";

        var editor = FindEditor(_window!);
        editor.Focus();
        editor.Text = expected;

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        Assert.That(editor.Text, Does.Contain(expected));
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

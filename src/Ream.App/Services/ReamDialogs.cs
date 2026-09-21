using System.Windows;
using Ream.App.Views;

namespace Ream.App.Services;

/// <summary>The file pickers. The real ones are Ream's own browser window (ThemedFileDialogs); tests use fakes so nothing ever shows.</summary>
internal interface IFileDialogs
{
    /// <summary>For New: where to put the new ream and what to call it. Null if cancelled.</summary>
    string? PickNewReam(string suggestedName, string initialDirectory);

    /// <summary>For Open: an existing .ream. Null if cancelled.</summary>
    string? PickOpenReam(string initialDirectory);

    /// <summary>For Save As: where to copy the ream and what to call the copy. Null if cancelled.</summary>
    string? PickSaveAs(string suggestedName, string initialDirectory);
}

internal enum SaveChoice { Save, DontSave, Cancel }

/// <summary>The questions Ream asks and the errors it reports.</summary>
internal interface IUserPrompts
{
    /// <summary>"Save changes to Foo?" for a ream with unsaved changes that is about to be left.</summary>
    SaveChoice AskSaveChanges(string reamName);

    /// <summary>A yes/no on something that removes content. The safe answer (No) is the default.</summary>
    bool Confirm(string title, string message, string confirmText);

    void ShowError(string title, string message);
}

internal sealed class WpfUserPrompts : IUserPrompts
{
    private readonly Func<string, string, PromptIcon, IReadOnlyList<PromptButton>, object?> _show;

    public WpfUserPrompts()
        : this((title, message, icon, buttons) => PromptWindow.Ask(Application.Current?.MainWindow, title, message, icon, buttons))
    {
    }

    /// <param name="show">How a prompt is put in front of the user and answered; tests pick a button instead of showing a window.</param>
    internal WpfUserPrompts(Func<string, string, PromptIcon, IReadOnlyList<PromptButton>, object?> show) => _show = show;

    public SaveChoice AskSaveChanges(string reamName)
    {
        var answer = _show(
            "Ream",
            $"Save changes to \"{reamName}\"?\nYour changes will be lost if you don't save.",
            PromptIcon.Question,
            [
                new PromptButton("_Save", SaveChoice.Save, IsDefault: true),
                new PromptButton("Do_n't save", SaveChoice.DontSave),
                new PromptButton("Cancel", SaveChoice.Cancel, IsCancel: true),
            ]);

        return answer is SaveChoice choice ? choice : SaveChoice.Cancel;
    }

    public bool Confirm(string title, string message, string confirmText) =>
        _show(
            title,
            message,
            PromptIcon.Warning,
            [
                new PromptButton(confirmText, true),
                new PromptButton("Cancel", false, IsDefault: true, IsCancel: true),
            ]) is true;

    public void ShowError(string title, string message) =>
        _show(title, message, PromptIcon.Error, [new PromptButton("OK", true, IsDefault: true, IsCancel: true)]);
}

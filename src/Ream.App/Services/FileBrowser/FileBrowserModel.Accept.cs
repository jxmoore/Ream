using Ream.Persistence.Storage;

namespace Ream.App.Services.FileBrowser;

internal sealed partial class FileBrowserModel
{
    /// <summary>
    /// The primary button / Enter in the name box. Open: the name must be an existing .ream file. Save: the name must be a valid,
    /// unused ream name; the result path always ends in ".ream" and never lands on an existing ream or its data folder.
    /// A name that is really a folder moves the browser there (NavigatedInstead) and empties the name box.
    /// </summary>
    public AcceptResult TryAccept() => Mode == BrowserMode.Open ? AcceptOpen() : AcceptSave();

    private AcceptResult AcceptOpen()
    {
        string text = CleanText(FileName);
        if (text.Length == 0) return AcceptResult.Reject("Choose a ream to open.");

        if (!TryResolve(text, out string full)) return AcceptResult.Reject($"\"{text}\" doesn't exist.");

        // "Foo" beside Foo.ream (and its data folder Foo) means the ream, not the folder.
        if (Path.GetExtension(full).Length == 0 && _fileSystem.FileExists(full + ReamPaths.Extension))
            return AcceptResult.Accept(full + ReamPaths.Extension);

        if (_fileSystem.FileExists(full))
            return ReamPaths.IsReamFile(full)
                ? AcceptResult.Accept(full)
                : AcceptResult.Reject($"\"{Path.GetFileName(full)}\" isn't a ream file (.ream).");

        if (_fileSystem.DirectoryExists(full)) return EnterFolder(full);

        return AcceptResult.Reject($"\"{text}\" doesn't exist.");
    }

    private AcceptResult AcceptSave()
    {
        string text = CleanText(FileName);
        if (text.Length == 0) return AcceptResult.Reject("Type a name for the ream.");

        bool hasDirectory = text.IndexOfAny(['\\', '/']) >= 0 || Path.IsPathRooted(text);
        string directory = _currentDirectory;
        string name = text;

        if (hasDirectory)
        {
            if (!TryResolve(text, out string full)) return AcceptResult.Reject($"\"{text}\" isn't a valid path.");
            if (_fileSystem.DirectoryExists(full)) return EnterFolder(full);

            string? parent = _fileSystem.ParentOf(full);
            if (parent is null || text.EndsWith('\\') || text.EndsWith('/'))
                return AcceptResult.Reject($"The folder \"{full}\" doesn't exist.");

            // The name comes from the typed text: resolving a path drops a trailing dot, and "Foo." must stay invalid.
            directory = parent;
            name = text[(text.LastIndexOfAny(['\\', '/']) + 1)..];
        }
        else if ((text.Trim('.').Length == 0 || !text.EndsWith('.')) && TryResolve(text, out string sibling) && _fileSystem.DirectoryExists(sibling))
        {
            return EnterFolder(sibling);
        }

        if (!_fileSystem.DirectoryExists(directory)) return AcceptResult.Reject($"The folder \"{directory}\" doesn't exist.");

        if (name.EndsWith(ReamPaths.Extension, StringComparison.OrdinalIgnoreCase)) name = name[..^ReamPaths.Extension.Length];

        string? problem = NameRules.Problem(name, "ream", NameRules.MaxReamNameLength)
            ?? (ReamPaths.IsValidName(name) ? null : "That isn't a valid name for a ream.");
        if (problem is not null) return AcceptResult.Reject(problem);

        string path = _fileSystem.Combine(directory, name + ReamPaths.Extension);
        if (_fileSystem.FileExists(path) || _fileSystem.DirectoryExists(_fileSystem.Combine(directory, name)))
            return AcceptResult.Reject($"There is already a ream called \"{name}\" here. Choose another name.");

        return AcceptResult.Accept(path);
    }

    private AcceptResult EnterFolder(string folder)
    {
        if (!Navigate(folder)) return AcceptResult.Reject($"\"{folder}\" doesn't exist.");

        FileName = "";
        return AcceptResult.Navigated(_currentDirectory);
    }
}

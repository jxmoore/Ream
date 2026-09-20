namespace Ream.App.Services.FileBrowser;

internal enum BrowserMode { Open, Save }

internal enum SortColumn { Name, Modified }

internal sealed record Breadcrumb(string Name, string Path);

internal enum AcceptOutcome
{
    /// <summary>The name is good: <see cref="AcceptResult.Path"/> is the full path of the ream. Close the dialog with it.</summary>
    Accepted,

    /// <summary>The name box held a folder: the browser is now in it (<see cref="AcceptResult.Path"/>) and the dialog stays open.</summary>
    NavigatedInstead,

    /// <summary>Nothing happens; show <see cref="AcceptResult.Error"/>.</summary>
    Rejected,
}

internal sealed record AcceptResult(AcceptOutcome Outcome, string? Path = null, string? Error = null)
{
    public bool IsAccepted => Outcome == AcceptOutcome.Accepted;

    public static AcceptResult Accept(string path) => new(AcceptOutcome.Accepted, path);

    public static AcceptResult Navigated(string directory) => new(AcceptOutcome.NavigatedInstead, directory);

    public static AcceptResult Reject(string error) => new(AcceptOutcome.Rejected, null, error);
}

internal enum NavigateOutcome
{
    /// <summary>The text was a folder and the browser is in it.</summary>
    Navigated,

    /// <summary>The text was a file: the browser is in its folder and the file is selected (and in the name box).</summary>
    SelectedFile,

    /// <summary>Nothing happened; show <see cref="NavigateResult.Error"/>.</summary>
    Invalid,
}

internal sealed record NavigateResult(NavigateOutcome Outcome, string? Error = null)
{
    public bool Succeeded => Outcome != NavigateOutcome.Invalid;
}

namespace Ream.App.Services;

/// <summary>
/// What the commands and key bindings ask of whatever manages the open ream (the <see cref="ReamManager"/>). Each returns
/// whether it went ahead: false means it was cancelled, refused or failed, and the user has already been told why.
/// </summary>
internal interface IReamFiles
{
    bool NewReam();
    bool OpenReam();
    bool Save();
    bool SaveAs();
    bool ClearReam();

    /// <summary>Turns auto-save on or off for good: applied at once and written to config.json.</summary>
    void SetAutoSave(bool on);

    /// <summary>Whether it is fine to leave the open ream now (closing the window): asks about unsaved changes if there are any.</summary>
    bool ConfirmLeave();
}

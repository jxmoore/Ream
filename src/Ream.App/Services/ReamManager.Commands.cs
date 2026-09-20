using Ream.App.ViewModels;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.App.Services;

// Open, Save, Save As and Clear. New, the swap and the shared checks are in ReamManager.cs.
internal sealed partial class ReamManager
{
    // ----- Open -----

    /// <summary>Asks which ream, and opens it. False if cancelled, refused, or the open ream's unsaved changes were not dealt with.</summary>
    public bool OpenReam()
    {
        string? picked = _dialogs.PickOpenReam(InitialDirectory());
        return picked is not null && OpenReam(picked);
    }

    /// <summary>Opens a specific .ream. A ream that will not open leaves the current one exactly as it was.</summary>
    public bool OpenReam(string path)
    {
        string full = Path.GetFullPath(path);

        if (!ReamPaths.IsReamFile(full))
        {
            _prompts.ShowError("Open ream", $"\"{Path.GetFileName(full)}\" isn't a ream file (.ream).");
            return false;
        }
        if (!File.Exists(full))
        {
            _prompts.ShowError("Open ream", $"\"{full}\" doesn't exist.");
            return false;
        }
        if (string.Equals(full, Current.ReamPath, StringComparison.OrdinalIgnoreCase)) return true; // already open

        // Read it before touching anything: a file that will not open must not cost the user their current ream.
        if (!ReamLauncher.TryOpen(full, out var opened, out string? error))
        {
            _prompts.ShowError("Open ream", $"Ream couldn't open \"{full}\".\n\n{error}");
            return false;
        }

        if (!ConfirmLeave()) return false;
        return SwapTo(opened!.Repository, opened.Snapshot);
    }

    // ----- Save -----

    /// <summary>Writes the ream now, whether or not auto-save is on. Tells the user if it could not.</summary>
    public bool Save()
    {
        if (Current.Save()) return true;

        _prompts.ShowError("Save", $"Ream couldn't save \"{Current.Name}\".\n\n{Current.Coordinator.LastError?.Message}");
        return false;
    }

    // ----- Save As -----

    /// <summary>
    /// Copies the whole ream to a new name and carries on working in the copy. The copy holds everything in memory (unsaved
    /// changes included); the original stays as it was last saved, so it doubles as a backup. Never overwrites a ream.
    /// </summary>
    public bool SaveAs()
    {
        string suggestion = Path.GetFileName(ReamPaths.UniquePath(InitialDirectory(), Current.Name + " copy"));
        string? picked = _dialogs.PickSaveAs(suggestion, InitialDirectory());
        if (picked is null) return false;

        string? path = FreePath(picked, "Save ream as");
        if (path is null) return false;

        DocumentRepository copy;
        DocumentSnapshot snapshot;
        try
        {
            if (Current.AutoSave) Current.Coordinator.Flush(); // the source on disk is up to date before it is copied

            ReamCopier.Copy(Current.ReamPath, path);
            copy = new DocumentRepository(path);
            try
            {
                copy.Save(SnapshotMapper.ToSnapshot(_app)); // what is in memory, into the copy
                snapshot = copy.Load();
            }
            catch
            {
                RemoveCreated(path);
                throw;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _prompts.ShowError("Save ream as", $"Ream couldn't save a copy as \"{path}\".\n\n{ex.Message}");
            return false;
        }

        return SwapTo(copy, snapshot, keepPosition: true);
    }

    /// <summary>Removes a ream this call just made (its file and data folder) after a failure part way through. Only ever used on a path that was free.</summary>
    private static void RemoveCreated(string reamPath)
    {
        try
        {
            string dataRoot = ReamPaths.DataRootOf(reamPath, ReamPaths.DefaultDataFolder(reamPath));
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
            if (File.Exists(reamPath)) File.Delete(reamPath);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't remove the partial copy '{reamPath}': {ex.Message}");
        }
    }

    // ----- Clear -----

    /// <summary>
    /// Removes every workspace and note, after asking. Nothing is deleted: saving the empty ream moves what disappeared into
    /// the ream's .trash folder, exactly as closing a single note does; and with auto-save off it is just an unsaved change
    /// that closing without saving undoes.
    /// </summary>
    public bool ClearReam()
    {
        var kept = _app.Workspaces
            .Select(w => (Workspace: w, Notes: w.Notes.Count(n => !n.IsBlankDraft())))
            .Where(w => w.Workspace.Name is not null || w.Notes > 0)
            .ToList();
        int notes = kept.Sum(w => w.Notes);

        if (kept.Count == 0 && notes == 0) return true; // nothing to clear

        string message = $"Clear \"{Current.Name}\"?\n\n{Count(kept.Count, "workspace")} and {Count(notes, "note")} will be removed. " +
                         "Removed notes are kept in the ream's .trash folder, not deleted.";
        if (!_prompts.Confirm("Clear ream", message, "Clear")) return false;

        _app.LoadReam([], 0, Current.Repository);
        _app.RequestEditorFocus();
        return true;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}

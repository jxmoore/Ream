using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ream.Core.Models;

/// <summary>
/// A short digest of what a snapshot *contains*: the workspaces (identity, name, order) and their notes (identity, order,
/// title, text, width). Two snapshots with the same fingerprint would put the same content on disk. It leaves out where the
/// user happened to be (focused note, current workspace, a note's fullscreen state), so moving around is not an edit.
/// </summary>
public static class SnapshotFingerprint
{
    public static string Of(DocumentSnapshot snapshot)
    {
        var text = new StringBuilder();

        foreach (var workspace in snapshot.Workspaces)
        {
            text.Append("W ").Append(workspace.Id.ToString("N")).Append(' ');
            Field(text, workspace.Name);
            Field(text, workspace.FolderName);
            text.Append('\n');

            foreach (var note in workspace.Notes)
            {
                text.Append("N ").Append(note.Id.ToString("N")).Append(' ');
                Field(text, note.Title);
                Field(text, note.CustomTitle);
                text.Append(Math.Round(note.WidthFraction, 4).ToString("R", CultureInfo.InvariantCulture)).Append(' ');
                Field(text, note.Body);
                text.Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Length-prefixed, so "ab"+"c" and "a"+"bc" (or null and "") can never look alike.</summary>
    private static void Field(StringBuilder text, string? value)
    {
        if (value is null) text.Append("~ ");
        else text.Append(value.Length).Append(':').Append(value).Append(' ');
    }
}

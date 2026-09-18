using System.Text;

namespace Ream.Persistence.Io;

/// <summary>Writes files so a crash mid-write can never leave a truncated file behind.</summary>
public static class AtomicFile
{
    private const int Attempts = 4;

    public static void WriteAllText(string path, string contents) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });

    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, stream => stream.Write(bytes, 0, bytes.Length));

    private static void Write(string path, Action<FileStream> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";

        // Sync/antivirus tools can briefly hold files open; retry transient sharing failures.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return;
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}

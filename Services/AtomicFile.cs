using System.IO;
using System.Text;

namespace AgentCompanion.Services;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        WriteAllText(path, content, createBackup: true);
    }

    public static void WriteAllTextWithoutBackup(string path, string content)
    {
        WriteAllText(path, content, createBackup: false);
    }

    private static void WriteAllText(string path, string content, bool createBackup)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The target directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = fullPath + ".bak";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(fullPath))
            {
                if (createBackup)
                    File.Replace(temporaryPath, fullPath, backupPath, true);
                else
                    File.Replace(temporaryPath, fullPath, null, true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

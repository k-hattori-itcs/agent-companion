using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AgentCompanion.Services;

internal static class AppDataPaths
{
    private static readonly string LegacyDataDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "pet_data");
    private static readonly string DataDirectoryValue = GetInstanceDataDirectory(
        AppDomain.CurrentDomain.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private static int _initialized;

    public static string DataDirectory => DataDirectoryValue;
    public static string PetsDirectory => Path.Combine(DataDirectoryValue, "pets");
    public static string ConfigJsonPath => Path.Combine(DataDirectoryValue, "pet_config.json");
    public static string ConfigCfgPath => Path.Combine(DataDirectoryValue, "pet_config.cfg");
    public static string TokenHistoryPath => Path.Combine(DataDirectoryValue, "token_history.json");
    public static string ProxyTargetsPath => Path.Combine(DataDirectoryValue, "proxy_targets.json");
    public static string AppLogPath => Path.Combine(DataDirectoryValue, "agentcompanion.log");
    public static string ProxyLogPath => Path.Combine(DataDirectoryValue, "debug.log");

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        Directory.CreateDirectory(DataDirectoryValue);
        try
        {
            MigrateLegacyFile("pet_config.json");
            MigrateLegacyFile("pet_config.cfg");
            MigrateLegacyFile("token_history.json");
            MigrateLegacyFile("proxy_targets.json");
            MigrateLegacyPets();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Legacy data migration was incomplete: {ex.GetType().Name}");
        }
    }

    internal static string GetInstanceDataDirectory(string executableDirectory, string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);

        var normalizedExecutableDirectory = Path.GetFullPath(executableDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedExecutableDirectory.ToUpperInvariant()));
        var instanceId = Convert.ToHexString(hash)[..16].ToLowerInvariant();
        return Path.Combine(Path.GetFullPath(localApplicationDataDirectory), "AgentCompanion", "instances", instanceId);
    }

    private static void MigrateLegacyFile(string fileName)
    {
        var source = Path.Combine(LegacyDataDirectory, fileName);
        var destination = Path.Combine(DataDirectoryValue, fileName);
        if (!File.Exists(source) || File.Exists(destination))
            return;

        File.Copy(source, destination, overwrite: false);
    }

    private static void MigrateLegacyPets()
    {
        var legacyPetsDirectory = Path.Combine(LegacyDataDirectory, "pets");
        if (!Directory.Exists(legacyPetsDirectory))
            return;

        Directory.CreateDirectory(PetsDirectory);
        foreach (var sourceDirectory in Directory.EnumerateDirectories(legacyPetsDirectory))
        {
            var name = Path.GetFileName(sourceDirectory);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".", StringComparison.Ordinal))
                continue;

            CopyMissingFiles(sourceDirectory, Path.Combine(PetsDirectory, name));
        }
    }

    private static void CopyMissingFiles(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destination = Path.GetFullPath(Path.Combine(destinationDirectory, relativePath));
            var root = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Legacy character file is outside the destination directory.");
            if (File.Exists(destination))
                continue;

            var destinationParent = Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("Character destination directory could not be resolved.");
            Directory.CreateDirectory(destinationParent);
            using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }
}

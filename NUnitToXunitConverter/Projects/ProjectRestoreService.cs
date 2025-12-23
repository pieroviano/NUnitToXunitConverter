namespace NUnitToXunitConverter.Projects;

internal static class ProjectRestoreService
{
    private const string ExternalFilesFolderName = "_ExternalFiles";

    public static void RestoreBackupIfExists(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileName(projectDir);

        var oldRoot = Path.GetFullPath(Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = Path.Combine(oldRoot, projectName);

        if (!Directory.Exists(backupProjectDir))
            return;

        Console.WriteLine($"Restoring backup from {backupProjectDir}");

        RestoreProjectCsFiles(projectDir, backupProjectDir);
        RestoreExternalFiles(backupProjectDir);
    }

    private static void RestoreProjectCsFiles(string projectDir, string backupProjectDir)
    {
        foreach (var backupFile in Directory.GetFiles(
                     backupProjectDir, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupProjectDir, backupFile);

            // Skip external files container
            if (relativePath.StartsWith(
                    ExternalFilesFolderName + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(projectDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(backupFile, destination, overwrite: true);
        }
    }

    private static void RestoreExternalFiles(string backupProjectDir)
    {
        var externalDir = Path.Combine(backupProjectDir, ExternalFilesFolderName);

        if (!Directory.Exists(externalDir))
            return;

        foreach (var file in Directory.GetFiles(externalDir, "*.cs", 
                     SearchOption.AllDirectories))
        {
            var originalPath = DecodeOriginalPath(externalDir, file);

            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.Copy(file, originalPath, overwrite: true);
        }
    }

    private static string DecodeOriginalPath(string externalRoot, string backupFilePath)
    {
        var relative = Path.GetRelativePath(externalRoot, backupFilePath);
        var parts = relative.Split(Path.DirectorySeparatorChar);

        var drive = parts[0].Replace('_', ':');
        var rest = Path.Combine(parts.Skip(1).ToArray());

        return Path.Combine(drive, rest);
    }
}

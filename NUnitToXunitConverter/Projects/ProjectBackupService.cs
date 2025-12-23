namespace NUnitToXunitConverter.Projects;

internal static class ProjectBackupService
{
    public static void CreateBackup(string csprojPath, IEnumerable<string> projectFiles)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileName(projectDir);

        var oldRoot = Path.GetFullPath(Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = Path.Combine(oldRoot, projectName);
        var externalFilesDir = Path.Combine(backupProjectDir, "_ExternalFiles");

        // Delete previous backup
        if (Directory.Exists(backupProjectDir))
        {
            Directory.Delete(backupProjectDir, true);
        }

        // Copy project directory
        CopyDirectory(projectDir, backupProjectDir);

        // Copy external files
        CopyExternalFiles(projectFiles, projectDir, externalFilesDir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, dest);
        }
    }

    private static void CopyExternalFiles(IEnumerable<string> projectFiles, string projectDir, 
        string externalFilesDir)
    {
        foreach (var file in projectFiles)
        {
            var fullPath = Path.GetFullPath(file);

            if (fullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = GetSafeRelativePath(fullPath);
            var destination = Path.Combine(externalFilesDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(fullPath, destination, true);
        }
    }

    private static string GetSafeRelativePath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)!;

        return fullPath
            .Substring(root.Length)
            .Replace(':', '_')
            .TrimStart(Path.DirectorySeparatorChar);
    }
}
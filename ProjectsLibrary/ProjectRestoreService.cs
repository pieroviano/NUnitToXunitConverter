using System.Diagnostics;

namespace ProjectsLibrary;

public class ProjectRestoreService
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;
    public Path Path { get; set; } = System.IO.InputOutput.Instance.Path;
    public Directory Directory { get; set; } = System.IO.InputOutput.Instance.Directory;

    private const string ExternalFilesFolderName = "_ExternalFiles";

    /// <summary>Restores the backup over the project. Returns whether there was one to restore.</summary>
    public bool RestoreBackupIfExists(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileName(projectDir);

        var oldRoot = Path.GetFullPath(Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = Path.Combine(oldRoot, projectName);

        if (!Directory.Exists(backupProjectDir))
            return false;

        LoggerFactoryContainer.Instance.LoggerFactory.Info($"Restoring backup from {backupProjectDir}");

        RestoreProjectFile(csprojPath, backupProjectDir);
        RestoreProjectCsFiles(projectDir, backupProjectDir);
        RestoreExternalFiles(backupProjectDir);

        return true;
    }

    /// <summary>
    /// The csproj is part of what a conversion rewrites, so leaving it out of the restore left the project
    /// half-converted - xUnit packages against unconverted sources - and let the next backup capture the
    /// already-converted csproj, losing the original for good.
    /// </summary>
    private void RestoreProjectFile(string csprojPath, string backupProjectDir)
    {
        var backupCsproj = Path.Combine(backupProjectDir, Path.GetFileName(csprojPath));

        if (File.Exists(backupCsproj))
            File.Copy(backupCsproj, csprojPath, overwrite: true);
    }

    private void RestoreProjectCsFiles(string projectDir, string backupProjectDir)
    {
        foreach (var backupFile in Directory.GetFiles(
                     backupProjectDir, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupProjectDir, backupFile);

            // Skip external files container
            if (relativePath.StartsWith(
                    ExternalFilesFolderName + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(projectDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(backupFile, destination, overwrite: true);
        }
    }

    private void RestoreExternalFiles(string backupProjectDir)
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

    private string DecodeOriginalPath(string externalRoot, string backupFilePath)
    {
        var relative = Path.GetRelativePath(externalRoot, backupFilePath);
        var parts = relative.Split(System.IO.Path.DirectorySeparatorChar);

        var drive = parts[0].Replace('_', ':');
        var rest = Path.Combine(parts.Skip(1).ToArray());

        return Path.Combine(drive, rest);
    }
}

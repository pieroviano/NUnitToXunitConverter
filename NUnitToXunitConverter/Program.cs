using System.Diagnostics;
using NUnitToXunitConverter.Conversion;
using NUnitToXUnitConverter.Conversion;
using ProjectsLibrary;

namespace NUnitToXunitConverter;

public class Program
{
    public static File File { get; set; } = System.IO.InputOutput.Instance.File;
    public static Path Path { get; set; } = System.IO.InputOutput.Instance.Path;

    public static int Main(string[] args)
    {
        LoggerFactoryContainer.Instance.LoggerFactory = ConsoleLoggerFactory.Instance;
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Info("Usage: NUnitToXunitConverter <path-to-csproj>");
            return 1;
        }

        var csprojPath = Path.GetFullPath(args[0]);

        // 1️ Restore previous backup if present
        new ProjectRestoreService().RestoreBackupIfExists(csprojPath);

        // 2️ Scan project AFTER restore
        var projectFiles = new NUnitFiles().GetNUnitCsFiles(csprojPath);

        // 3️ Create fresh backup
        new ProjectBackupService().CreateBackup(csprojPath, projectFiles);

        // 4️ Run conversion
        foreach (var file in projectFiles)
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Info($"Converting: {file}");
            new NUnitToXunitRewriter().RewriteFile(file);
        }

        LoggerFactoryContainer.Instance.LoggerFactory.Info("Conversion complete.");
        return 0;
    }
}
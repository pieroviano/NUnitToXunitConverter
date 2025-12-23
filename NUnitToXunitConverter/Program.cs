using NUnitToXunitConverter.Conversion;
using NUnitToXunitConverter.Projects;

namespace NUnitToXunitConverter;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.WriteLine("Usage: NUnitToXunitConverter <path-to-csproj>");
            return 1;
        }

        var csprojPath = Path.GetFullPath(args[0]);

        // 1️ Restore previous backup if present
        ProjectRestoreService.RestoreBackupIfExists(csprojPath);

        // 2️ Scan project AFTER restore
        var projectFiles = ProjectScanner.GetNUnitCsFiles(csprojPath);

        // 3️ Create fresh backup
        ProjectBackupService.CreateBackup(csprojPath, projectFiles);

        // 4️ Run conversion
        foreach (var file in projectFiles)
        {
            Console.WriteLine($"Converting: {file}");
            NUnitToXunitRewriter.RewriteFile(file);
        }

        Console.WriteLine("Conversion complete.");
        return 0;
    }
}
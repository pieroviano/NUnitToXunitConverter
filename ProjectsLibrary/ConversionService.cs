using System.Diagnostics;
using ProjectsLibrary.Conversion;

namespace ProjectsLibrary
{
    public class ConversionService
    {
        public void DoConversion(string csprojPath, bool forceMsTestProject)
        {
            // 1️ Restore previous backup if present
            new ProjectRestoreService().RestoreBackupIfExists(csprojPath);

            // 2️ Scan project AFTER restore
            var projectFiles = new UnitTestsFiles(new NUnitTestDetector()).GetUnitTestCsFiles(csprojPath);
            if (projectFiles.Length == 0 || forceMsTestProject)
            {
                projectFiles = new UnitTestsFiles(new MsUnitTestDetector()).GetUnitTestCsFiles(csprojPath);
            }

            // 3️ Create fresh backup
            new ProjectBackupService().CreateBackup(csprojPath, projectFiles);

            // 4️ Point the project at the xUnit packages
            if (projectFiles.Length > 0 && new TestPackagesRewriter().RewritePackageReferences(csprojPath))
            {
                LoggerFactoryContainer.Instance.LoggerFactory.Info($"Updated test packages: {csprojPath}");
            }

            // 5️ Run conversion
            foreach (var file in projectFiles)
            {
                LoggerFactoryContainer.Instance.LoggerFactory.Info($"Converting: {file}");
                new NUnitToXunitRewriter().RewriteFile(file);
            }

            LoggerFactoryContainer.Instance.LoggerFactory.Info("Conversion complete.");
        }
    }
}

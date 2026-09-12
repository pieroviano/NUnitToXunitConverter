using System.Diagnostics;
using ProjectsLibrary.Conversion;

namespace ProjectsLibrary
{
    public class ConversionService
    {
        public void DoConversion(string csprojPath, bool forceMsTestProject)
        {
            var logger = LoggerFactoryContainer.Instance.LoggerFactory;

            logger.Info($"Converting project: {csprojPath}");

            // 1️ Restore previous backup if present
            new ProjectRestoreService().RestoreBackupIfExists(csprojPath);

            // 2️ Scan project AFTER restore
            var projectFiles = new UnitTestsFiles(new NUnitTestDetector()).GetUnitTestCsFiles(csprojPath);

            if (projectFiles.Length == 0 || forceMsTestProject)
            {
                logger.Info(forceMsTestProject
                    ? "Scanning for MSTest test files: the MSTest project conversion was forced."
                    : "No NUnit test files found, scanning for MSTest test files instead.");

                projectFiles = new UnitTestsFiles(new MsUnitTestDetector()).GetUnitTestCsFiles(csprojPath);
            }

            if (projectFiles.Length == 0)
            {
                logger.Warn($"No MSTest or NUnit test files found in {csprojPath}, nothing to convert.");
            }
            else
            {
                logger.Info($"Found {projectFiles.Length} test file(s) to convert.");
            }

            // 3️ Create fresh backup
            new ProjectBackupService().CreateBackup(csprojPath, projectFiles);
            logger.Info("Backup created.");

            // 4️ Point the project at the xUnit packages
            if (projectFiles.Length > 0)
            {
                logger.Info(new TestPackagesRewriter().RewritePackageReferences(csprojPath)
                    ? $"Updated test packages: {csprojPath}"
                    : $"Test packages already reference xUnit, left unchanged: {csprojPath}");
            }

            // 5️ Run conversion
            foreach (var file in projectFiles)
            {
                logger.Info($"Converting: {file}");
                new NUnitToXunitRewriter().RewriteFile(file);
            }

            logger.Info($"Conversion complete, {projectFiles.Length} file(s) rewritten.");
        }
    }
}

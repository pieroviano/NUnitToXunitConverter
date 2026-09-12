using System.Diagnostics;
using ConversionClassLibrary;
using ConversionClassLibrary.Interfaces;
using ProjectsLibrary.Conversion;

namespace ProjectsLibrary
{
    public class ConversionService : IConversionService
    {
        public ConversionResult DoConversion(string csprojPath, bool forceMsTestProject)
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
                // Nothing will be written, so there is nothing to back up either. Skipping matters for a
                // solution-wide run, which would otherwise leave an ../Old/<ProjectName> beside every
                // non-test project it walked past.
                logger.Warn($"No MSTest or NUnit test files found in {csprojPath}, nothing to convert.");

                return new ConversionResult(csprojPath, [], false);
            }

            logger.Info($"Found {projectFiles.Length} test file(s) to convert.");

            // 3️ Create fresh backup
            new ProjectBackupService().CreateBackup(csprojPath, projectFiles);
            logger.Info("Backup created.");

            // Steps 4 and 5 are the only ones that write to the project. A failure part way through would
            // otherwise leave a half-converted mixture of xUnit packages and unconverted sources, so the
            // backup taken above is put back before the failure is allowed to propagate.
            bool packagesUpdated;

            try
            {
                // 4️ Point the project at the xUnit packages
                packagesUpdated = new TestPackagesRewriter().RewritePackageReferences(csprojPath);

                logger.Info(packagesUpdated
                    ? $"Updated test packages: {csprojPath}"
                    : $"Test packages already reference xUnit, left unchanged: {csprojPath}");

                // 5️ Run conversion
                foreach (var file in projectFiles)
                {
                    logger.Info($"Converting: {file}");
                    new NUnitToXunitRewriter().RewriteFile(file);
                }
            }
            catch
            {
                logger.Warn($"Conversion failed, rolling the project back to the backup: {csprojPath}");

                try
                {
                    new ProjectRestoreService().RestoreBackupIfExists(csprojPath);
                }
                catch (Exception restoreException)
                {
                    // Whatever went wrong here, the original failure is the one worth reporting: letting
                    // this one escape would replace the cause with a symptom.
                    logger.Error(
                        restoreException,
                        $"Rollback failed, the project is left part-converted: {csprojPath}");
                }

                throw;
            }

            logger.Info($"Conversion complete, {projectFiles.Length} file(s) rewritten.");

            return new ConversionResult(csprojPath, projectFiles, packagesUpdated);
        }
    }
}

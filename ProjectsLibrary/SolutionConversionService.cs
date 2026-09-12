using System.Diagnostics;
using ConversionClassLibrary;
using ConversionClassLibrary.Interfaces;
using ProjectsLibrary.Conversion;

namespace ProjectsLibrary;

/// <summary>
/// Runs <see cref="ConversionService"/> across every project a solution declares. A project that throws is
/// recorded and the run carries on: one unconvertible project should not abandon the rest of the solution,
/// and <see cref="ConversionService"/> has already rolled that project back by the time the failure lands here.
/// </summary>
public class SolutionConversionService : ISolutionConversionService
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;
    public Path Path { get; set; } = System.IO.InputOutput.Instance.Path;

    public ISolutionScanner SolutionScanner { get; set; } = new SolutionScanner();
    public IConversionService ConversionService { get; set; } = new ConversionService();
    public ITestPackagesRewriter TestPackagesRewriter { get; set; } = new TestPackagesRewriter();

    public SolutionConversionResult ConvertSolution(string solutionPath, bool forceMsTestProject)
    {
        var logger = LoggerFactoryContainer.Instance.LoggerFactory;

        var projects = GetProjects(solutionPath);
        logger.Info($"Converting solution: {solutionPath} ({projects.Count} project(s))");

        var outcomes = new List<ProjectConversionOutcome>();

        foreach (var project in projects)
        {
            if (!File.Exists(project))
            {
                logger.Warn($"Declared by the solution but missing on disk: {project}");
                outcomes.Add(new ProjectConversionOutcome(project, 0, false, "Project file not found."));

                continue;
            }

            if (!IsTestProject(project))
            {
                logger.Info($"Not a test project, left alone: {project}");
                outcomes.Add(new ProjectConversionOutcome(project, 0, false));

                continue;
            }

            try
            {
                var result = ConversionService.DoConversion(project, forceMsTestProject);

                outcomes.Add(new ProjectConversionOutcome(
                    project,
                    result.ConvertedFiles.Count,
                    result.PackagesUpdated));
            }
            catch (Exception exception)
            {
                logger.Error(exception, $"Conversion failed, continuing with the rest: {project}");
                outcomes.Add(new ProjectConversionOutcome(project, 0, false, exception.Message));
            }
        }

        var solutionResult = new SolutionConversionResult(solutionPath, outcomes);

        logger.Info(
            $"Solution complete: {solutionResult.ConvertedProjects} project(s) converted, " +
            $"{solutionResult.ConvertedFiles} file(s) rewritten, {solutionResult.FailedProjects} failed.");

        return solutionResult;
    }

    public IReadOnlyList<TestProjectInfo> ListTestProjects(string solutionPath)
    {
        var infos = new List<TestProjectInfo>();

        foreach (var project in GetProjects(solutionPath))
        {
            if (!File.Exists(project) || !IsTestProject(project))
            {
                infos.Add(new TestProjectInfo(project, null, 0));

                continue;
            }

            // The same order DoConversion uses, so what is reported is what a conversion would act on.
            var nunit = new UnitTestsFiles(new NUnitTestDetector()).GetUnitTestCsFiles(project);

            if (nunit.Length > 0)
            {
                infos.Add(new TestProjectInfo(project, "NUnit", nunit.Length));

                continue;
            }

            var msTest = new UnitTestsFiles(new MsUnitTestDetector()).GetUnitTestCsFiles(project);

            infos.Add(new TestProjectInfo(project, msTest.Length > 0 ? "MSTest" : null, msTest.Length));
        }

        return infos;
    }

    public IReadOnlyList<string> RestoreBackups(string solutionOrProjectPath)
    {
        var projects = IsProject(solutionOrProjectPath)
            ? [Path.GetFullPath(solutionOrProjectPath)]
            : GetProjects(solutionOrProjectPath);

        return projects
            .Where(project => new ProjectRestoreService().RestoreBackupIfExists(project))
            .ToList();
    }

    /// <summary>
    /// Whether a solution-wide run should touch this project at all. A project file that cannot be parsed is
    /// not a project worth converting, and saying so beats failing the whole solution over it.
    /// </summary>
    private bool IsTestProject(string csprojPath)
    {
        try
        {
            return TestPackagesRewriter.ReferencesTestPackages(csprojPath);
        }
        catch (Exception exception)
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Warn(
                $"Could not read {csprojPath}, treating it as a non-test project: {exception.Message}");

            return false;
        }
    }

    private IReadOnlyList<string> GetProjects(string solutionPath)
    {
        return IsProject(solutionPath)
            ? [Path.GetFullPath(solutionPath)]
            : SolutionScanner.GetProjects(solutionPath).ToList();
    }

    /// <summary>A csproj is accepted anywhere a solution is, so one project can be converted on its own.</summary>
    private bool IsProject(string path)
    {
        return Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    }
}

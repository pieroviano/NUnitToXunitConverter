namespace ConversionClassLibrary.Interfaces;

/// <summary>Applies the conversion across every project in a solution.</summary>
public interface ISolutionConversionService
{
    ISolutionScanner SolutionScanner { get; set; }
    IConversionService ConversionService { get; set; }

    SolutionConversionResult ConvertSolution(string solutionPath, bool forceMsTestProject);

    /// <summary>Reports which projects hold tests, and in which framework, without changing anything.</summary>
    IReadOnlyList<TestProjectInfo> ListTestProjects(string solutionPath);

    /// <summary>
    /// Restores the <c>../Old/&lt;ProjectName&gt;</c> backup over every project of a solution, or over one
    /// project when handed a csproj. Returns the projects that actually had a backup.
    /// </summary>
    IReadOnlyList<string> RestoreBackups(string solutionOrProjectPath);
}

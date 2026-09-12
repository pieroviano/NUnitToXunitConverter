namespace ConversionClassLibrary;

/// <summary>What converting one project did.</summary>
/// <param name="ProjectPath">The csproj that was converted.</param>
/// <param name="ConvertedFiles">The test sources that were rewritten; empty when the project holds none.</param>
/// <param name="PackagesUpdated">Whether the csproj's package references were swapped for the xUnit set.</param>
public record ConversionResult(
    string ProjectPath,
    IReadOnlyList<string> ConvertedFiles,
    bool PackagesUpdated)
{
    /// <summary>Whether anything was actually rewritten.</summary>
    public bool Converted => ConvertedFiles.Count > 0;
}

/// <summary>
/// One project's outcome within a solution-wide run. <paramref name="Error"/> is set when that project threw;
/// the run carries on, so one broken project does not abandon the rest.
/// </summary>
public record ProjectConversionOutcome(
    string ProjectPath,
    int FilesConverted,
    bool PackagesUpdated,
    string? Error = null)
{
    /// <summary>Nothing to do: the project holds no MSTest or NUnit test files.</summary>
    public bool Skipped => Error == null && FilesConverted == 0;
}

/// <summary>The result of converting every project in a solution.</summary>
public record SolutionConversionResult(
    string SolutionPath,
    IReadOnlyList<ProjectConversionOutcome> Projects)
{
    public int ConvertedProjects => Projects.Count(p => p is { Error: null, FilesConverted: > 0 });
    public int ConvertedFiles => Projects.Sum(p => p.FilesConverted);
    public int FailedProjects => Projects.Count(p => p.Error != null);
}

/// <summary>What a scan found in one project, without changing anything.</summary>
/// <param name="Framework">"NUnit", "MSTest", or <see langword="null"/> when neither was detected.</param>
public record TestProjectInfo(string ProjectPath, string? Framework, int TestFiles);

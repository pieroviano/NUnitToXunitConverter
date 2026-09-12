using System.ComponentModel;
using ConversionClassLibrary;
using ConversionClassLibrary.Interfaces;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter.Mcp;

/// <summary>
/// The MCP surface over <see cref="SolutionConversionService"/>. These tools rewrite the caller's sources in
/// place, which is what the <c>Destructive</c> and <c>ReadOnly</c> hints tell a client: only
/// <c>list_test_projects</c> is safe to call speculatively.
/// </summary>
[McpServerToolType]
public static class ConversionTools
{
    [McpServerTool(
        Name = "convert_solution",
        Title = "Convert a solution's tests to xUnit",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("""
        Converts every MSTest or NUnit test project in a solution (.sln or .slnx) to xUnit, in place.
        Each project is backed up to ../Old/<ProjectName> first, so re-running is repeatable rather than
        cumulative, and a project that fails is rolled back and reported without stopping the rest.
        Projects containing no test files are left completely untouched.
        """)]
    public static SolutionConversionResult ConvertSolution(
        [Description("Absolute path to the .sln or .slnx file. A .csproj is also accepted.")]
        string solutionPath,
        [Description("Skip NUnit detection and scan for MSTest test files directly.")]
        bool forceMsTest = false)
    {
        return Service().ConvertSolution(Existing(solutionPath), forceMsTest);
    }

    [McpServerTool(
        Name = "list_test_projects",
        Title = "List the test projects in a solution",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("""
        Reports which projects of a solution currently contain MSTest or NUnit test files, and how many,
        without changing anything. Use it to see what convert_solution would act on. A project already
        converted to xUnit reports no framework and no test files, because neither detector matches it.
        """)]
    public static IReadOnlyList<TestProjectInfo> ListTestProjects(
        [Description("Absolute path to the .sln or .slnx file. A .csproj is also accepted.")]
        string solutionPath)
    {
        return Service().ListTestProjects(Existing(solutionPath));
    }

    [McpServerTool(
        Name = "convert_project",
        Title = "Convert one project's tests to xUnit",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("""
        Converts a single .csproj to xUnit, in place - the same thing the command line tool does. The project
        is backed up to ../Old/<ProjectName> first and rolled back if the conversion fails part way through.
        """)]
    public static ConversionResult ConvertProject(
        [Description("Absolute path to the .csproj file.")]
        string csprojPath,
        [Description("Skip NUnit detection and scan for MSTest test files directly.")]
        bool forceMsTest = false)
    {
        return new ConversionService().DoConversion(Existing(csprojPath), forceMsTest);
    }

    [McpServerTool(
        Name = "restore_backup",
        Title = "Undo a conversion from its backup",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("""
        Restores the ../Old/<ProjectName> backup over every project of a solution, or over a single project
        when given a .csproj, undoing a conversion. Returns the projects that actually had a backup.
        """)]
    public static IReadOnlyList<string> RestoreBackup(
        [Description("Absolute path to a .sln, .slnx or .csproj file.")]
        string path)
    {
        return Service().RestoreBackups(Existing(path));
    }

    private static ISolutionConversionService Service()
    {
        return new SolutionConversionService();
    }

    /// <summary>
    /// A path that does not exist is the caller's mistake, not a conversion failure, so it is rejected up
    /// front with a message naming the path rather than surfacing as an IO exception from deep inside.
    /// </summary>
    private static string Existing(string path)
    {
        // McpException is the one exception type whose message the SDK passes through to the client;
        // anything else is reported as a bare "An error occurred invoking '<tool>'".
        if (string.IsNullOrWhiteSpace(path))
            throw new McpException("A path is required.");

        var full = System.IO.Path.GetFullPath(path);

        if (!System.IO.File.Exists(full))
            throw new McpException($"No such file: {full}");

        return full;
    }
}

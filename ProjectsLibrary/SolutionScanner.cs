using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConversionClassLibrary.Interfaces;

namespace ProjectsLibrary;

/// <summary>
/// Reads the project list out of either solution format: the XML <c>.slnx</c> Visual Studio writes now, and
/// the classic <c>.sln</c> text format. Both are parsed textually - MSBuild is not asked to evaluate the
/// solution, so a project that does not restore still shows up.
/// </summary>
public class SolutionScanner : ISolutionScanner
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;
    public Path Path { get; set; } = System.IO.InputOutput.Instance.Path;

    /// <summary>
    /// <c>Project("{type guid}") = "Name", "Relative\Path.csproj", "{project guid}"</c>. Solution folders use
    /// the same line shape with their name in the path slot, so the .csproj suffix is what tells them apart.
    /// </summary>
    private static readonly Regex SlnProjectLine = new(
        @"^Project\("".*?""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+)""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<string> GetProjects(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        var declared = Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(solutionPath)
            : ReadSln(solutionPath);

        return declared
            .Where(IsCsharpProject)
            .Select(relative => Path.GetFullPath(Path.Combine(solutionDir, Normalise(relative))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> ReadSlnx(string solutionPath)
    {
        var document = XDocument.Parse(File.ReadAllText(solutionPath));

        // Projects sit either at the root or inside any depth of <Folder>, so take every descendant.
        return document
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }

    private IEnumerable<string> ReadSln(string solutionPath)
    {
        return SlnProjectLine
            .Matches(File.ReadAllText(solutionPath))
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    private static bool IsCsharpProject(string path)
    {
        return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Solution files always use backslashes, which are just characters on Linux and macOS.</summary>
    private static string Normalise(string relativePath)
    {
        return relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar);
    }
}

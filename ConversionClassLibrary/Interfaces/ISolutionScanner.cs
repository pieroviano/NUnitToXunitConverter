namespace ConversionClassLibrary.Interfaces;

/// <summary>Reads the project list out of a solution file.</summary>
public interface ISolutionScanner
{
    File File { get; set; }
    Path Path { get; set; }

    /// <summary>
    /// The absolute path of every C# project the solution declares, in declaration order. Solution folders
    /// and non-C# projects are left out.
    /// </summary>
    IEnumerable<string> GetProjects(string solutionPath);
}

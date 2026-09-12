using NSubstitute;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter.Tests;

public class SolutionScannerTests
{
    /// <summary>
    /// Only the file contents are substituted: the path helpers are pure, and running them for real is what
    /// makes the relative-path resolution worth asserting.
    /// </summary>
    private static SolutionScanner ScannerOver(string solutionPath, string content)
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText(solutionPath).Returns(content);

        return new SolutionScanner { File = file };
    }

    [Fact]
    public void Slnx_Projects_Are_Read_From_The_Root_And_From_Any_Folder()
    {
        var solution = @"C:\repo\My.slnx";

        var projects = ScannerOver(solution, """
            <Solution>
              <Folder Name="/Solution Items/">
                <File Path=".gitignore" />
              </Folder>
              <Folder Name="/Tests/">
                <Project Path="A.Tests/A.Tests.csproj" />
              </Folder>
              <Project Path="Lib/Lib.csproj" />
            </Solution>
            """).GetProjects(solution).ToList();

        Assert.Equal(
            [@"C:\repo\A.Tests\A.Tests.csproj", @"C:\repo\Lib\Lib.csproj"],
            projects);
    }

    [Fact]
    public void Sln_Project_Lines_Are_Read_And_Solution_Folders_Skipped()
    {
        var solution = @"C:\repo\My.sln";

        // The second entry is a solution folder: same line shape, but its "path" is just the folder name.
        var projects = ScannerOver(solution, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Tests", "Tests", "{26406}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "A.Tests", "A.Tests\A.Tests.csproj", "{98423}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Lib", "Lib\Lib.csproj", "{F3942}"
            EndProject
            Global
            EndGlobal
            """).GetProjects(solution).ToList();

        Assert.Equal(
            [@"C:\repo\A.Tests\A.Tests.csproj", @"C:\repo\Lib\Lib.csproj"],
            projects);
    }

    [Fact]
    public void Non_Csharp_Projects_Are_Left_Out()
    {
        var solution = @"C:\repo\My.sln";

        var projects = ScannerOver(solution, """
            Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "Native", "Native\Native.vcxproj", "{1}"
            EndProject
            Project("{F184B08F-C81C-45F6-A57F-5ABD9991F28F}") = "Legacy", "Legacy\Legacy.vbproj", "{2}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Lib", "Lib\Lib.csproj", "{3}"
            EndProject
            """).GetProjects(solution).ToList();

        Assert.Equal([@"C:\repo\Lib\Lib.csproj"], projects);
    }

    [Fact]
    public void A_Project_Listed_Twice_Is_Returned_Once()
    {
        var solution = @"C:\repo\My.slnx";

        var projects = ScannerOver(solution, """
            <Solution>
              <Folder Name="/Tests/">
                <Project Path="A.Tests/A.Tests.csproj" />
              </Folder>
              <Project Path="A.Tests/A.Tests.csproj" />
            </Solution>
            """).GetProjects(solution).ToList();

        Assert.Equal([@"C:\repo\A.Tests\A.Tests.csproj"], projects);
    }

    [Fact]
    public void Paths_Are_Resolved_Against_The_Solution_Directory()
    {
        var solution = @"C:\repo\src\My.slnx";

        var projects = ScannerOver(solution, """
            <Solution>
              <Project Path="../tools/Tool.csproj" />
            </Solution>
            """).GetProjects(solution).ToList();

        Assert.Equal([@"C:\repo\tools\Tool.csproj"], projects);
    }

    [Fact]
    public void An_Empty_Solution_Yields_Nothing()
    {
        var solution = @"C:\repo\My.slnx";

        Assert.Empty(ScannerOver(solution, "<Solution />").GetProjects(solution));
    }
}

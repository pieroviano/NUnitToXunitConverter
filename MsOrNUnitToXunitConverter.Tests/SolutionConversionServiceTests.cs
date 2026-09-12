using ConversionClassLibrary;
using ConversionClassLibrary.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter.Tests;

public class SolutionConversionServiceTests
{
    private const string Solution = @"C:\repo\My.slnx";
    private const string TestProject = @"C:\repo\A.Tests\A.Tests.csproj";
    private const string Library = @"C:\repo\Lib\Lib.csproj";

    private static SolutionConversionService CreateSut(
        out IConversionService conversion,
        out ITestPackagesRewriter packages,
        params string[] projects)
    {
        var scanner = Substitute.For<ISolutionScanner>();
        scanner.GetProjects(Solution).Returns(projects);

        var file = Substitute.For<IFile>();
        file.Exists(Arg.Any<string>()).Returns(true);

        conversion = Substitute.For<IConversionService>();

        // Everything is a test project unless a test says otherwise.
        packages = Substitute.For<ITestPackagesRewriter>();
        packages.ReferencesTestPackages(Arg.Any<string>()).Returns(true);

        return new SolutionConversionService
        {
            File = file,
            SolutionScanner = scanner,
            ConversionService = conversion,
            TestPackagesRewriter = packages
        };
    }

    [Fact]
    public void Every_Test_Project_In_The_Solution_Is_Converted()
    {
        var sut = CreateSut(out var conversion, out _, TestProject, Library);

        conversion.DoConversion(Arg.Any<string>(), false)
            .Returns(call => new ConversionResult(call.Arg<string>(), ["one.cs", "two.cs"], true));

        var result = sut.ConvertSolution(Solution, false);

        Assert.Equal(2, result.ConvertedProjects);
        Assert.Equal(4, result.ConvertedFiles);
        Assert.Equal(0, result.FailedProjects);
        conversion.Received(1).DoConversion(TestProject, false);
        conversion.Received(1).DoConversion(Library, false);
    }

    [Fact]
    public void A_Project_That_References_No_Test_Framework_Is_Never_Opened()
    {
        var sut = CreateSut(out var conversion, out var packages, TestProject, Library);

        // The file-level detectors are substring sniffs, so an ordinary library that merely mentions
        // "[TestFixture]" in a string literal must not be handed to them at all.
        packages.ReferencesTestPackages(Library).Returns(false);
        conversion.DoConversion(TestProject, false)
            .Returns(new ConversionResult(TestProject, ["one.cs"], true));

        var result = sut.ConvertSolution(Solution, false);

        conversion.DidNotReceive().DoConversion(Library, Arg.Any<bool>());
        Assert.Equal(1, result.ConvertedProjects);

        var skipped = Assert.Single(result.Projects, p => p.ProjectPath == Library);
        Assert.True(skipped.Skipped);
        Assert.Null(skipped.Error);
    }

    [Fact]
    public void A_Project_That_Throws_Is_Recorded_And_The_Rest_Still_Run()
    {
        var sut = CreateSut(out var conversion, out _, TestProject, Library);

        conversion.DoConversion(TestProject, false).Throws(new InvalidOperationException("boom"));
        conversion.DoConversion(Library, false)
            .Returns(new ConversionResult(Library, ["one.cs"], true));

        var result = sut.ConvertSolution(Solution, false);

        Assert.Equal(1, result.FailedProjects);
        Assert.Equal("boom", Assert.Single(result.Projects, p => p.ProjectPath == TestProject).Error);

        // The failure did not abandon the solution.
        Assert.Equal(1, result.ConvertedProjects);
        conversion.Received(1).DoConversion(Library, false);
    }

    [Fact]
    public void A_Project_Declared_But_Missing_Is_Reported_Rather_Than_Converted()
    {
        var sut = CreateSut(out var conversion, out _, TestProject);
        sut.File.Exists(TestProject).Returns(false);

        var result = sut.ConvertSolution(Solution, false);

        conversion.DidNotReceive().DoConversion(Arg.Any<string>(), Arg.Any<bool>());
        Assert.Equal("Project file not found.", Assert.Single(result.Projects).Error);
    }

    [Fact]
    public void The_ForceMsTest_Flag_Reaches_Every_Project()
    {
        var sut = CreateSut(out var conversion, out _, TestProject, Library);

        conversion.DoConversion(Arg.Any<string>(), true)
            .Returns(call => new ConversionResult(call.Arg<string>(), [], false));

        sut.ConvertSolution(Solution, true);

        conversion.Received(1).DoConversion(TestProject, true);
        conversion.Received(1).DoConversion(Library, true);
    }

    [Fact]
    public void A_Csproj_Is_Accepted_Where_A_Solution_Is_And_Skips_The_Scanner()
    {
        var sut = CreateSut(out var conversion, out _);

        conversion.DoConversion(TestProject, false)
            .Returns(new ConversionResult(TestProject, ["one.cs"], true));

        var result = sut.ConvertSolution(TestProject, false);

        Assert.Equal(1, result.ConvertedProjects);
        sut.SolutionScanner.DidNotReceive().GetProjects(Arg.Any<string>());
    }

    [Fact]
    public void ListTestProjects_Reports_Nothing_For_A_Project_Without_Test_Packages()
    {
        var sut = CreateSut(out _, out var packages, Library);
        packages.ReferencesTestPackages(Library).Returns(false);

        var info = Assert.Single(sut.ListTestProjects(Solution));

        Assert.Null(info.Framework);
        Assert.Equal(0, info.TestFiles);
    }
}

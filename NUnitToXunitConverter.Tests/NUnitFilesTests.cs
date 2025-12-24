using System.Linq;
using NSubstitute;
using Xunit;
using NUnitToXUnitConverter.Conversion;
using ConversionClassLibrary.Interfaces;

namespace NUnitToXunitConverter.Tests;

public class NUnitFilesTests
{
    [Fact]
    public void GetNUnitCsFiles_FiltersByDetector_AndOrdersOneTimeSetUpFirst()
    {
        // Arrange
        var sut = new NUnitFiles();

        var scanner = Substitute.For<IProjectScanner>();
        var detector = Substitute.For<IUnitTestDetector>();

        // Use NSubstitute to provide a substitute for the File type used by the implementation.
        // The production File type is referenced unqualified in the implementation; substitute it here.
        var file = Substitute.For<IFile>();

        sut.ProjectScanner = scanner;
        sut.NUnitTestDetector = detector;
        sut.File = file;

        var csprojPath = "path/to/project.csproj";

        var f1 = "C:\\proj\\A.cs"; // contains OneTimeSetUp -> should be ordered first
        var f2 = "C:\\proj\\B.cs"; // not a test
        var f3 = "C:\\proj\\C.cs"; // regular test

        scanner.GetCsFiles(csprojPath).Returns(new[] { f1, f2, f3 });

        detector.IsNUnitTest(f1).Returns(true);
        detector.IsNUnitTest(f2).Returns(false);
        detector.IsNUnitTest(f3).Returns(true);

        file.ReadAllText(f1).Returns("using NUnit.Framework; class A { [OneTimeSetUp] public void Init() {} }");
        file.ReadAllText(f3).Returns("using NUnit.Framework; class C { [Test] public void T() {} }");

        // Act
        var result = sut.GetNUnitCsFiles(csprojPath);

        // Assert
        Assert.Equal(2, result.Length);
        // f1 should appear before f3 because it contains [OneTimeSetUp] and the implementation orders those first
        Assert.Equal(f1, result[0]);
        Assert.Equal(f3, result[1]);

        // verify the dependencies were called as expected
        scanner.Received(1).GetCsFiles(csprojPath);
        detector.Received(1).IsNUnitTest(f1);
        detector.Received(1).IsNUnitTest(f2);
        detector.Received(1).IsNUnitTest(f3);
        file.Received(1).ReadAllText(f1);
        file.Received(1).ReadAllText(f3);
    }

    [Fact]
    public void GetNUnitCsFiles_ReturnsEmpty_WhenNoNUnitFilesFound()
    {
        // Arrange
        var sut = new NUnitFiles();

        var scanner = Substitute.For<IProjectScanner>();
        var detector = Substitute.For<IUnitTestDetector>();
        var file = Substitute.For<IFile>();

        sut.ProjectScanner = scanner;
        sut.NUnitTestDetector = detector;
        sut.File = file;

        var csprojPath = "p.csproj";
        var files = new[] { "A.cs", "B.cs" };

        scanner.GetCsFiles(csprojPath).Returns(files);

        detector.IsNUnitTest(Arg.Any<string>()).Returns(false);

        // Act
        var result = sut.GetNUnitCsFiles(csprojPath);

        // Assert
        Assert.Empty(result);

        scanner.Received(1).GetCsFiles(csprojPath);
        detector.Received(1).IsNUnitTest("A.cs");
        detector.Received(1).IsNUnitTest("B.cs");
        // No reads should be performed because detector returned false for all
        file.DidNotReceive().ReadAllText(Arg.Any<string>());
    }
}
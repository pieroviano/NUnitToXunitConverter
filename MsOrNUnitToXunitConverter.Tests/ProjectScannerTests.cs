using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using Xunit;
using ProjectsLibrary;

namespace NUnitToXunitConverter.Tests;

public class ProjectScannerTests
{
    [Fact]
    public void GetCsFiles_RespectsCompileIncludeAndRemove_AndFiltersByExists()
    {
        // Arrange
        var projectDir = Path.Combine(Path.GetTempPath(), "ps_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        var csprojPath = Path.Combine(projectDir, "test.csproj");
        var includeA = "A.cs";
        var includeB = "B.cs";

        // csproj with two Compile items, B is also removed
        var csprojXml = $@"
<Project>
  <ItemGroup>
    <Compile Include=""{includeA}"" />
    <Compile Include=""{includeB}"" Remove=""{includeB}"" />
  </ItemGroup>
</Project>";
        File.WriteAllText(csprojPath, csprojXml);

        var sut = new ProjectScanner();

        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        sut.File = file;
        sut.Path = path;
        sut.Directory = directory;

        // Behavior for path helpers used by ProjectScanner
        path.GetDirectoryName(csprojPath).Returns(projectDir);
        path.Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1]));
        path.GetFullPath(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFullPath((string)ci[0]));
        path.GetDirectoryName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetDirectoryName((string)ci[0]));
        path.GetFileName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFileName((string)ci[0]));

        // Simulate existence: A exists, B does not (either removed or missing)
        var fullA = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, includeA));
        var fullB = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, includeB));

        file.Exists(fullA).Returns(true);
        file.Exists(fullB).Returns(false);

        // Act
        var results = sut.GetCsFiles(csprojPath).ToArray();

        // Assert: only A should be returned (B is removed or does not exist)
        Assert.Single(results);
        Assert.Equal(fullA, results[0]);

        // verify interactions
        path.Received().GetDirectoryName(csprojPath);
        file.Received(1).Exists(fullA);

        // Cleanup
        try { Directory.Delete(projectDir, true); } catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public void GetCsFiles_UsesSdkImplicitInclude_WhenNoCompileItems()
    {
        // Arrange
        var projectDir = Path.Combine(Path.GetTempPath(), "ps_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);

        var csprojPath = Path.Combine(projectDir, "sdk.csproj");
        // no Compile items → triggers SDK-style implicit include fallback
        File.WriteAllText(csprojPath, "<Project></Project>");

        var sut = new ProjectScanner();

        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        sut.File = file;
        sut.Path = path;
        sut.Directory = directory;

        path.GetDirectoryName(csprojPath).Returns(projectDir);
        path.Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1]));
        path.GetFullPath(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFullPath((string)ci[0]));
        path.GetDirectoryName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetDirectoryName((string)ci[0]));
        path.GetFileName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFileName((string)ci[0]));

        // Prepare file list returned by Directory.GetFiles for SDK fallback
        var srcFile = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "Src.cs"));
        var objFile = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "obj", "Generated.cs"));

        // Directory should return both files; ProjectScanner should filter out obj dir files and non-.cs
        directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Returns(new[] { srcFile, objFile });

        // Directory.Exists used in other code paths; default to true when asked (safe)
        directory.Exists(Arg.Any<string>()).Returns(true);

        // Make Path.Combine for obj dir consistent
        var objDir = System.IO.Path.Combine(projectDir, "obj") + System.IO.Path.DirectorySeparatorChar;
        path.Combine(projectDir, "obj").Returns(objDir);

        // Existence: only srcFile exists
        file.Exists(srcFile).Returns(true);
        file.Exists(objFile).Returns(true); // existence is true but should be filtered out by obj-dir check

        // Act
        var results = sut.GetCsFiles(csprojPath).ToArray();

        // Assert: only srcFile should be returned (obj file lives under obj directory and is excluded)
        Assert.Single(results.Distinct());
        Assert.Equal(srcFile, results[0]);

        // verify calls
        directory.Received(1).GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
        file.Received(1).Exists(srcFile);

        // Cleanup
        try { Directory.Delete(projectDir, true); } catch { /* ignore cleanup errors */ }
    }
}
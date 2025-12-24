using System;
using System.IO;
using System.Linq;
using NSubstitute;
using Xunit;
using ProjectsLibrary;

namespace NUnitToXunitConverter.Tests;

public class ProjectRestoreServiceTests
{

    [Fact]
    public void RestoreBackupIfExists_DoesNothing_WhenNoBackupPresent()
    {
        // Arrange
        var csprojPath = @"C:\repo\NoBackupProj\p.csproj";
        var projectDir = @"C:\repo\NoBackupProj";
        var projectName = "NoBackupProj";

        var sut = new ProjectRestoreService();

        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        sut.File = file;
        sut.Path = path;
        sut.Directory = directory;

        path.GetDirectoryName(csprojPath).Returns(projectDir);
        path.Combine(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1]));
        path.Combine(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1], (string)ci[2]));
        path.GetFullPath(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFullPath((string)ci[0]));

        var oldRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = System.IO.Path.Combine(oldRoot, projectName);

        // No backup exists
        directory.Exists(backupProjectDir).Returns(false);

        // Act
        sut.RestoreBackupIfExists(csprojPath);

        // Assert - no file copies or directory enumerations should occur
        directory.DidNotReceive().GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>());
        file.DidNotReceive().Copy(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }
}
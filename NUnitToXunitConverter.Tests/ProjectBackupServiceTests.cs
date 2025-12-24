using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using Xunit;
using ProjectsLibrary;

namespace NUnitToXunitConverter.Tests;

public class ProjectBackupServiceTests
{
    [Fact]
    public void CreateBackup_DeletesPreviousBackup_AndCopiesProjectAndExternalFiles()
    {
        // Arrange
        var csprojPath = @"C:\proj\project.csproj";
        var projectDir = @"C:\proj";
        var projectName = "proj";

        var sut = new ProjectBackupService();

        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        sut.File = file;
        sut.Path = path;
        sut.Directory = directory;

        // Path helpers
        path.GetDirectoryName(csprojPath).Returns(projectDir);
        path.Combine(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1]));
        path.Combine(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci => System.IO.Path.Combine((string)ci[0], (string)ci[1], (string)ci[2]));
        path.GetFullPath(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFullPath((string)ci[0]));
        path.GetFileName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFileName((string)ci[0]));
        path.GetPathRoot(Arg.Any<string>()).Returns(ci => System.IO.Path.GetPathRoot((string)ci[0]));

        // Compute expected paths using real System.IO.Path to mirror method logic
        var oldRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = System.IO.Path.Combine(oldRoot, projectName);
        var externalFilesDir = System.IO.Path.Combine(backupProjectDir, "_ExternalFiles");

        // Simulate that a previous backup exists so it will be deleted
        directory.Exists(backupProjectDir).Returns(true);

        // Project directory contents
        var topFile = System.IO.Path.Combine(projectDir, "A.cs");
        var subDir = System.IO.Path.Combine(projectDir, "Sub");
        var subFile = System.IO.Path.Combine(subDir, "B.cs");

        directory.GetFiles(projectDir).Returns(new[] { topFile });
        directory.GetDirectories(projectDir).Returns(new[] { subDir });
        directory.GetFiles(subDir).Returns(new[] { subFile });
        directory.GetDirectories(subDir).Returns(new string[0]);

        // External files list contains one internal (should be skipped) and one external (should be copied)
        var externalFile = @"D:\external\ext.cs";
        var projectFiles = new[] { topFile, externalFile };

        // File.Exists is not used by ProjectBackupService, but File.Copy will be asserted instead.
        // Act
        sut.CreateBackup(csprojPath, projectFiles);

        // Assert: previous backup deleted
        directory.Received(1).Delete(backupProjectDir, true);

        // Assert: project directory copied (create root backup folder)
        directory.Received(1).CreateDirectory(backupProjectDir);

        // Assert: top-level file copied to backup folder
        var expectedTopDest = System.IO.Path.Combine(backupProjectDir, System.IO.Path.GetFileName(topFile));
        file.Received(1).Copy(topFile, expectedTopDest, true);

        // Assert: sub-directory created and sub-file copied preserving file name
        var expectedSubDestDir = System.IO.Path.Combine(backupProjectDir, System.IO.Path.GetFileName(subDir));
        directory.Received().CreateDirectory(expectedSubDestDir);
        var expectedSubFileDest = System.IO.Path.Combine(expectedSubDestDir, System.IO.Path.GetFileName(subFile));
        file.Received(1).Copy(subFile, expectedSubFileDest, true);

        // Assert: external file copied into external files directory with safe relative path
        var root = System.IO.Path.GetPathRoot(externalFile)!; // e.g. "D:\"
        var relative = externalFile.Substring(root.Length).Replace(':', '_').TrimStart(System.IO.Path.DirectorySeparatorChar);
        var expectedExternalDest = System.IO.Path.Combine(externalFilesDir, relative);
        file.Received(1).Copy(externalFile, expectedExternalDest, true);
    }

    [Fact]
    public void CreateBackup_DoesNotDeleteWhenNoPreviousBackupExists()
    {
        // Arrange
        var csprojPath = @"C:\p\proj.csproj";
        var projectDir = @"C:\p";
        var projectName = "p";

        var sut = new ProjectBackupService();

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
        path.GetFileName(Arg.Any<string>()).Returns(ci => System.IO.Path.GetFileName((string)ci[0]));

        var oldRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "Old"));
        var backupProjectDir = System.IO.Path.Combine(oldRoot, projectName);

        // No previous backup
        directory.Exists(backupProjectDir).Returns(false);

        // No files or dirs in project — still should create backup folder and not attempt delete
        directory.GetFiles(projectDir).Returns(new string[0]);
        directory.GetDirectories(projectDir).Returns(new string[0]);

        sut.CreateBackup(csprojPath, Array.Empty<string>());

        // Assert: delete not called
        directory.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<bool>());

        // Assert: backup root created
        directory.Received(1).CreateDirectory(backupProjectDir);
    }
}
using System.Diagnostics;
using MsOrNUnitToXunitConverter;
using NSubstitute;

namespace MsOrNUnitToXunitConverter.Tests;

public class ProgramTests
{
    /// <summary>
    /// Main replaces the process-wide logger factory and reads its file system through static properties, so
    /// everything it touches is put back afterwards.
    /// </summary>
    private static int RunMain(string[] args, IFile? file = null, IPath? path = null)
    {
        var previousFactory = LoggerFactoryContainer.Instance.LoggerFactory;
        var previousFile = Program.File;
        var previousPath = Program.Path;

        try
        {
            if (file != null)
                Program.File = file;

            if (path != null)
                Program.Path = path;

            return Program.Main(args);
        }
        finally
        {
            Program.File = previousFile;
            Program.Path = previousPath;
            LoggerFactoryContainer.Instance.LoggerFactory = previousFactory;
        }
    }

    [Fact]
    public void A_Missing_Argument_Reports_Usage_And_Exits_One()
    {
        Assert.Equal(1, RunMain([]));
    }

    [Fact]
    public void An_Argument_That_Is_Not_A_File_Reports_Usage_And_Exits_One()
    {
        var file = Substitute.For<IFile>();
        file.Exists(Arg.Any<string>()).Returns(false);

        Assert.Equal(1, RunMain(["nope.csproj"], file));
    }

    [Fact]
    public void A_Conversion_That_Throws_Is_Reported_Instead_Of_Crashing_The_Process()
    {
        var file = Substitute.For<IFile>();
        file.Exists(Arg.Any<string>()).Returns(true);

        // A directory passed off as the csproj: the scan reads it as a file and throws.
        var directory = System.IO.Path.GetDirectoryName(typeof(ProgramTests).Assembly.Location)!;

        var path = Substitute.For<IPath>();
        path.GetFullPath(Arg.Any<string>()).Returns(directory);

        Assert.Equal(1, RunMain(["whatever.csproj"], file, path));
    }
}

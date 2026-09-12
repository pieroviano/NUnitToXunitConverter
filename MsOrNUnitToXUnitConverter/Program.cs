using System.Diagnostics;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter;

public class Program
{
    public static File File { get; set; } = System.IO.InputOutput.Instance.File;
    public static Path Path { get; set; } = System.IO.InputOutput.Instance.Path;

    public static int Main(string[] args)
    {
        LoggerFactoryContainer.Instance.LoggerFactory = ConsoleLoggerFactory.Instance;
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Info("Usage: MsOrNUnitToXunitConverter <path-to-csproj>");
            return 1;
        }

        var csprojPath = Path.GetFullPath(args[0]);

        new ConversionService().DoConversion(csprojPath, false);
        return 0;
    }
}
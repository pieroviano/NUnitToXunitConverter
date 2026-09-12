using System.Diagnostics;
using MsOrNUnitToXunitConverter.Logging;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter;

public class Program
{
    public static File File { get; set; } = System.IO.InputOutput.Instance.File;
    public static Path Path { get; set; } = System.IO.InputOutput.Instance.Path;

    public static int Main(string[] args)
    {
        LoggerFactoryContainer.Instance.LoggerFactory = SerilogLoggerFactory.Instance;
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Info("Usage: MsOrNUnitToXunitConverter <path-to-csproj>");
            return 1;
        }

        var csprojPath = Path.GetFullPath(args[0]);

        try
        {
            new ConversionService().DoConversion(csprojPath, false);
        }
        catch (Exception exception)
        {
            // Reported rather than left to crash the process, so the message lands in the same log as every
            // other step. DoConversion has already rolled the project back to its backup by this point.
            LoggerFactoryContainer.Instance.LoggerFactory.Fatal(exception, $"Conversion failed: {csprojPath}");

            return 1;
        }

        return 0;
    }
}
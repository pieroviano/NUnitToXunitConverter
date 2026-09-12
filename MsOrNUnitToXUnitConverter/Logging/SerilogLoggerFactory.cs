using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace MsOrNUnitToXunitConverter.Logging;

/// <summary>
/// An <see cref="ILoggerFactory"/> backed by Serilog, so everything the conversion pipeline already logs
/// through <see cref="LoggerFactoryContainer"/> reaches the console without any library having to know about
/// Serilog. Mirrors <see cref="ConsoleLoggerFactory"/>: a singleton <see cref="Instance"/> handing out a fresh
/// logger per <see cref="GetLogger"/> call.
/// </summary>
public class SerilogLoggerFactory : ILoggerFactory
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly Serilog.ILogger _logger;

    /// <summary>
    /// Writes to the console, <see cref="DiagnosticLevel.Error"/> and above to standard error. Serilog is
    /// left at its most verbose so that <see cref="MinimumLoggingLevel"/> stays the single place verbosity is
    /// decided. The console sink is synchronous, so nothing needs flushing at exit.
    /// </summary>
    public SerilogLoggerFactory()
        : this(new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(
                outputTemplate: OutputTemplate,
                standardErrorFromLevel: LogEventLevel.Error)
            .CreateLogger())
    {
    }

    /// <summary>
    /// Writes to a Serilog logger the caller configured, whatever its sinks. That logger's own minimum level
    /// applies on top of <see cref="MinimumLoggingLevel"/>.
    /// </summary>
    public SerilogLoggerFactory(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public static ILoggerFactory Instance { get; } = new SerilogLoggerFactory();

    /// <summary>
    /// Net4x's <see cref="LoggerFactoryExtensions"/> log a level strictly above this one, so
    /// <see cref="DiagnosticLevel.Default"/> - the lowest value other than
    /// <see cref="DiagnosticLevel.None"/> - lets every level reach Serilog. It matters that this is not the
    /// <see cref="ConsoleLoggerFactory"/> default of <see cref="DiagnosticLevel.Error"/>, which discards
    /// everything the conversion reports short of <see cref="DiagnosticLevel.Fatal"/>.
    /// </summary>
    public DiagnosticLevel MinimumLoggingLevel { get; set; } = DiagnosticLevel.Default;

    public System.Diagnostics.ILogger GetLogger(System.Threading.Thread? thread = null)
    {
        return new SerilogLogger(_logger);
    }

    public void SetLogger()
    {
        LoggerFactoryContainer.Instance.LoggerFactory = this;
    }
}

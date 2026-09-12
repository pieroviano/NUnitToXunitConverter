using System.Diagnostics;
using Serilog.Events;

namespace MsOrNUnitToXunitConverter.Logging;

/// <summary>
/// Writes <see cref="System.Diagnostics.ILogger"/> entries to a Serilog logger. Deriving from
/// <see cref="LoggerWithEvents"/> and routing through <c>DoLog</c> keeps the <c>BeforeLogging</c>/<c>Logged</c>
/// events behaving exactly as they do for <see cref="ConsoleLogger"/>.
/// </summary>
public class SerilogLogger : LoggerWithEvents, System.Diagnostics.ILogger
{
    private readonly Serilog.ILogger _logger;

    public SerilogLogger(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public void Log(DiagnosticLevel level, Exception? exception, string? message, string? title)
    {
        DoLog(level, exception, message, title, Write);
    }

    public Task LogAsync(DiagnosticLevel level, Exception? exception, string? message, string? title)
    {
        return DoLogAsync(level, exception, message, title, (l, e, m, t) =>
        {
            Write(l, e, m, t);

            return Task.CompletedTask;
        });
    }

    internal static LogEventLevel ToSerilogLevel(DiagnosticLevel level)
    {
        return level switch
        {
            DiagnosticLevel.Trace => LogEventLevel.Verbose,
            DiagnosticLevel.Debug => LogEventLevel.Debug,
            DiagnosticLevel.Warning => LogEventLevel.Warning,
            DiagnosticLevel.Error => LogEventLevel.Error,
            DiagnosticLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    private void Write(DiagnosticLevel level, Exception? exception, string? message, string? title)
    {
        // The Net4x abstraction hands over an already formatted string, so there is no message template to
        // preserve; ":l" keeps Serilog from quoting it as a captured value.
        if (string.IsNullOrEmpty(title))
            _logger.Write(ToSerilogLevel(level), exception, "{Message:l}", message);
        else
            _logger.Write(ToSerilogLevel(level), exception, "{Title:l}: {Message:l}", title, message);
    }
}

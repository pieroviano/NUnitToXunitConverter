using System.Diagnostics;
using MsOrNUnitToXunitConverter.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>
/// Covers the adapter - how an <see cref="System.Diagnostics.ILogger"/> entry is turned into a Serilog one.
/// One quirk of the Net4x side shapes these tests: <c>LoggerWithEvents.DoLog</c>, which the synchronous path
/// goes through, drops anything at or below the minimum level of the factory installed in
/// <see cref="LoggerFactoryContainer"/> - NOT the factory the logger came from. Under the test runner that is
/// the default <see cref="ConsoleLoggerFactory"/> with its minimum of <see cref="DiagnosticLevel.Error"/>, so
/// the mapping and formatting tests go through <c>LogAsync</c>, which does not consult it, and the tests that
/// do exercise the synchronous path install the factory first.
/// </summary>
public class SerilogLoggerFactoryTests
{
    /// <summary>Captures what the adapter hands to Serilog, instead of writing it to the console.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    private static (System.Diagnostics.ILogger Logger, CapturingSink Sink) CreateLogger()
    {
        var sink = new CapturingSink();

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new SerilogLoggerFactory(serilog).GetLogger(), sink);
    }

    [Fact]
    public async Task A_Message_Reaches_Serilog()
    {
        var (logger, sink) = CreateLogger();

        await logger.LogAsync(DiagnosticLevel.Information, null, "converting something", null);

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Information, logEvent.Level);
        Assert.Equal("converting something", logEvent.RenderMessage());
    }

    [Theory]
    [InlineData(DiagnosticLevel.Trace, LogEventLevel.Verbose)]
    [InlineData(DiagnosticLevel.Debug, LogEventLevel.Debug)]
    [InlineData(DiagnosticLevel.Information, LogEventLevel.Information)]
    [InlineData(DiagnosticLevel.Warning, LogEventLevel.Warning)]
    [InlineData(DiagnosticLevel.Error, LogEventLevel.Error)]
    [InlineData(DiagnosticLevel.Fatal, LogEventLevel.Fatal)]
    public async Task Every_Diagnostic_Level_Maps_Onto_Its_Serilog_Level(
        DiagnosticLevel level,
        LogEventLevel expected)
    {
        var (logger, sink) = CreateLogger();

        await logger.LogAsync(level, null, "message", null);

        Assert.Equal(expected, Assert.Single(sink.Events).Level);
    }

    [Fact]
    public async Task A_Title_Is_Prefixed_To_The_Message()
    {
        var (logger, sink) = CreateLogger();

        await logger.LogAsync(DiagnosticLevel.Information, null, "the message", "the title");

        Assert.Equal("the title: the message", Assert.Single(sink.Events).RenderMessage());
    }

    [Fact]
    public async Task An_Exception_Is_Handed_To_Serilog_Rather_Than_Flattened_Into_The_Text()
    {
        var (logger, sink) = CreateLogger();

        var exception = new InvalidOperationException("boom");
        await logger.LogAsync(DiagnosticLevel.Error, exception, "conversion failed", null);

        var logEvent = Assert.Single(sink.Events);
        Assert.Same(exception, logEvent.Exception);
        Assert.Equal("conversion failed", logEvent.RenderMessage());
    }

    [Fact]
    public async Task A_Message_Is_Not_Quoted_As_A_Captured_Value()
    {
        var (logger, sink) = CreateLogger();

        await logger.LogAsync(DiagnosticLevel.Information, null, @"Converting: C:\proj\MyTests.cs", null);

        // ":l" in the template - a quoted, escaped rendering would be useless in a console log.
        Assert.Equal(@"Converting: C:\proj\MyTests.cs", Assert.Single(sink.Events).RenderMessage());
    }

    [Fact]
    public void An_Installed_Factory_Carries_The_Pipeline_Steps_Through_To_Serilog()
    {
        // What ConversionService actually does, and the regression this factory exists to fix: with
        // ConsoleLoggerFactory installed, every one of these was dropped before reaching a sink.
        var sink = WhileInstalled(factory =>
        {
            factory.Info("Converting project: Some.csproj");
            factory.Info("Converting: MathTests.cs");
            factory.Warn("No MSTest or NUnit test files found");
            factory.GetLogger().Log(DiagnosticLevel.Information, null, "through ILogger.Log", null);
        });

        Assert.Equal(
            [
                "Converting project: Some.csproj",
                "Converting: MathTests.cs",
                "No MSTest or NUnit test files found",
                "through ILogger.Log"
            ],
            sink.Events.Select(e => e.RenderMessage()));
    }

    /// <summary>
    /// Runs <paramref name="log"/> with a capturing factory installed in the container, which is what makes
    /// the synchronous path's level check pass, and restores the previous factory afterwards.
    /// </summary>
    private static CapturingSink WhileInstalled(Action<SerilogLoggerFactory> log)
    {
        var sink = new CapturingSink();

        var factory = new SerilogLoggerFactory(
            new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());

        var previous = LoggerFactoryContainer.Instance.LoggerFactory;

        try
        {
            factory.SetLogger();
            log(factory);
        }
        finally
        {
            LoggerFactoryContainer.Instance.LoggerFactory = previous;
        }

        return sink;
    }

    [Fact]
    public async Task A_Cancelled_BeforeLogging_Handler_Suppresses_The_Entry()
    {
        var (logger, sink) = CreateLogger();

        logger.BeforeLogging += (_, e) => e.Cancel = true;

        await logger.LogAsync(DiagnosticLevel.Information, null, "suppressed", null);

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task A_Logged_Handler_Sees_What_Was_Written()
    {
        var (logger, _) = CreateLogger();

        LoggingInfo? logged = null;
        logger.Logged += (_, e) => logged = e.Value;

        await logger.LogAsync(DiagnosticLevel.Warning, null, "watch out", null);

        Assert.NotNull(logged);
        Assert.Equal("watch out", logged.Message);
        Assert.Equal(DiagnosticLevel.Warning, logged.Level);
    }

    [Fact]
    public void The_Factory_Does_Not_Inherit_The_Console_Factory_Minimum_Of_Error()
    {
        // ConsoleLoggerFactory defaults to Error, which is why the converter printed nothing at all: every
        // step it reports is logged at Information.
        Assert.Equal(DiagnosticLevel.Error, ConsoleLoggerFactory.Instance.MinimumLoggingLevel);
        Assert.Equal(DiagnosticLevel.Default, new SerilogLoggerFactory().MinimumLoggingLevel);
    }

    [Fact]
    public void Each_GetLogger_Call_Hands_Out_A_Logger()
    {
        var factory = new SerilogLoggerFactory(new LoggerConfiguration().CreateLogger());

        Assert.IsType<SerilogLogger>(factory.GetLogger());
        Assert.NotSame(factory.GetLogger(), factory.GetLogger());
    }

    [Fact]
    public void SetLogger_Installs_The_Factory_In_The_Container()
    {
        var previous = LoggerFactoryContainer.Instance.LoggerFactory;

        try
        {
            var factory = new SerilogLoggerFactory(new LoggerConfiguration().CreateLogger());

            factory.SetLogger();

            Assert.Same(factory, LoggerFactoryContainer.Instance.LoggerFactory);
        }
        finally
        {
            LoggerFactoryContainer.Instance.LoggerFactory = previous;
        }
    }
}

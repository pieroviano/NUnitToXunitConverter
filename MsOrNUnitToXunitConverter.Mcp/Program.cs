using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MsOrNUnitToXunitConverter.Logging;
using Serilog;
using Serilog.Events;

// An MCP stdio server speaks JSON-RPC over stdout, so anything else written there corrupts the protocol.
// Both logging stacks therefore have to be pointed away from it: Serilog at stderr, and the host's own
// providers cleared - Host.CreateApplicationBuilder installs a console provider that writes to stdout.
LoggerFactoryContainer.Instance.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            standardErrorFromLevel: LogEventLevel.Verbose)
        .CreateLogger());

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

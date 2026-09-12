using System.Diagnostics;
using MsOrNUnitToXunitConverter.Logging;
using ProjectsLibrary;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>
/// Drives the real pipeline against a throwaway project on disk. <see cref="ConversionService"/> constructs
/// its collaborators itself, so a failure has to be provoked through the file system rather than substituted.
/// </summary>
public class ConversionServiceRollbackTests
{
    private const string MsTestSource = """
        using Microsoft.VisualStudio.TestTools.UnitTesting;

        [TestClass]
        public class PLACEHOLDER
        {
            [TestMethod]
            public void Works() { Assert.AreEqual(1, 1); }
        }
        """;

    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
          <ItemGroup><PackageReference Include="MSTest.TestFramework" Version="3.0.0" /></ItemGroup>
        </Project>
        """;

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    [Fact]
    public void A_Failure_Part_Way_Through_Rolls_The_Project_Back_And_Reports_The_Original_Cause()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MsOrNUnitToXunitConverter.Tests",
            Guid.NewGuid().ToString("N"));

        var projectDir = System.IO.Path.Combine(root, "Proj");
        System.IO.Directory.CreateDirectory(projectDir);

        var csproj = System.IO.Path.Combine(projectDir, "Proj.csproj");
        var first = System.IO.Path.Combine(projectDir, "ATests.cs");
        var second = System.IO.Path.Combine(projectDir, "BTests.cs");

        System.IO.File.WriteAllText(csproj, Csproj);
        System.IO.File.WriteAllText(first, MsTestSource.Replace("PLACEHOLDER", "ATests"));
        System.IO.File.WriteAllText(second, MsTestSource.Replace("PLACEHOLDER", "BTests"));

        // Rewriting the second file fails, after the packages and the first file have already been changed.
        System.IO.File.SetAttributes(second, System.IO.FileAttributes.ReadOnly);

        var sink = new CapturingSink();
        var previousFactory = LoggerFactoryContainer.Instance.LoggerFactory;

        try
        {
            // An installed factory with no level floor, so the pipeline's own log lines can be asserted and
            // nothing reaches the real console.
            LoggerFactoryContainer.Instance.LoggerFactory = new SerilogLoggerFactory(
                new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());

            var thrown = Assert.ThrowsAny<Exception>(
                () => new ConversionService().DoConversion(csproj, false));

            // The failure that surfaces is the one that stopped the conversion, not anything the rollback hit
            // on its way back out.
            Assert.Contains("BTests.cs", thrown.Message);

            // The csproj is restored first, so this holds however the rest of the rollback goes.
            Assert.Contains("MSTest.TestFramework", System.IO.File.ReadAllText(csproj));
            Assert.DoesNotContain("xunit", System.IO.File.ReadAllText(csproj));

            var firstContent = System.IO.File.ReadAllText(first);
            Assert.Contains("[TestClass]", firstContent);
            Assert.DoesNotContain("[Fact]", firstContent);

            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("rolling the project back"));
        }
        finally
        {
            LoggerFactoryContainer.Instance.LoggerFactory = previousFactory;

            // The backup copy inherited the read-only attribute, so clear it everywhere before deleting.
            foreach (var file in System.IO.Directory.GetFiles(
                         root, "*", System.IO.SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
            }

            System.IO.Directory.Delete(root, recursive: true);
        }
    }
}

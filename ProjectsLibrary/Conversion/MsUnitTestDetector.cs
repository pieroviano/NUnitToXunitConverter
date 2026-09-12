using ConversionClassLibrary.Interfaces;

namespace ProjectsLibrary.Conversion;

public class MsUnitTestDetector : IUnitTestDetector
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    public bool IsUnitTest(string file)
    {
        var text = File.ReadAllText(file);

        return text.Contains("using Microsoft.VisualStudio.TestTools.UnitTesting;")
               || text.Contains("[TestClass]")
               || text.Contains("[TestMethod]")
               || text.Contains("[TestCleanup]")
               || text.Contains("[TestInitialize]")
               || text.Contains("[TestClassInitialize]")
               || text.Contains("[TestClassCleanup]")
               // An assembly-settings file holds no test at all, so nothing selected it and it was left
               // referencing MSTest after the packages were swapped.
               || text.Contains("[assembly: Parallelize")
               || text.Contains("[assembly: DoNotParallelize")
               || text.Contains("[assembly: ClassCleanupExecution");
    }
}
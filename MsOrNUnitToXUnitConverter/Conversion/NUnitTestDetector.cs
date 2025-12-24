using ConversionClassLibrary.Interfaces;

namespace MsOrNUnitToXunitConverter.Conversion;

public class NUnitTestDetector : IUnitTestDetector
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    public bool IsUnitTest(string file)
    {
        var text = File.ReadAllText(file);

        return text.Contains("NUnit.Framework")
               || text.Contains("[Test]")
               || text.Contains("[TestFixture]")
               || text.Contains("[TestCase]");
    }
}
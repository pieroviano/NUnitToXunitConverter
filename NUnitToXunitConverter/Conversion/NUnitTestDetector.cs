namespace NUnitToXunitConverter.Conversion;

static class NUnitTestDetector
{
    public static bool IsNUnitTest(string file)
    {
        var text = File.ReadAllText(file);

        return text.Contains("NUnit.Framework")
               || text.Contains("[Test]")
               || text.Contains("[TestFixture]")
               || text.Contains("[TestCase]");
    }
}
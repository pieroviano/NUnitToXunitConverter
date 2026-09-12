using ConversionClassLibrary.Interfaces;

namespace ProjectsLibrary.Conversion;

public class UnitTestsFiles : IUnitTestsFiles
{
    public UnitTestsFiles(IUnitTestDetector unitTestDetector)
    {
        UnitTestDetector = unitTestDetector;
    }

    public File File { get; set; } = System.IO.InputOutput.Instance.File;
    public IProjectScanner ProjectScanner { get; set; } = new ProjectScanner();
    public IUnitTestDetector UnitTestDetector { get; set; }

    public string[] GetUnitTestCsFiles(string csprojPath)
    {
        return ProjectScanner.GetCsFiles(csprojPath).Where(UnitTestDetector.IsUnitTest).OrderBy(f => File.ReadAllText(f).Contains("[OneTimeSetUp]") ? "_____.cs" : f).ToArray();
    }
}
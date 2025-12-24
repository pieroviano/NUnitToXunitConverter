namespace ConversionClassLibrary.Interfaces;

public interface IUnitTestsFiles
{
    File File { get; set; }
    IProjectScanner ProjectScanner { get; set; }
    IUnitTestDetector UnitTestDetector { get; set; }
    string[] GetUnitTestCsFiles(string csprojPath);
}
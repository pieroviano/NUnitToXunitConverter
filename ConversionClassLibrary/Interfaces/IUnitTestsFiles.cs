namespace ConversionClassLibrary.Interfaces;

public interface IUnitTestsFiles
{
    File File { get; set; }
    IProjectScanner ProjectScanner { get; set; }
    IUnitTestDetector NUnitTestDetector { get; set; }
    string[] GetNUnitCsFiles(string csprojPath);
}
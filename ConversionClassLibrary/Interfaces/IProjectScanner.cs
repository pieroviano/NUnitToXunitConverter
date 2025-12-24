namespace ConversionClassLibrary.Interfaces;

public interface IProjectScanner
{
    File File { get; set; }
    Path Path { get; set; }
    Directory Directory { get; set; }
    IEnumerable<string> GetCsFiles(string csprojPath);
}
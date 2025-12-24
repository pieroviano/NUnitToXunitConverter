namespace ConversionClassLibrary.Interfaces;

public interface IUnitTestDetector
{
    File File { get; set; }
    bool IsUnitTest(string file);
}
using ConversionClassLibrary.Interfaces;
using NUnitToXunitConverter.Conversion;
using ProjectsLibrary;

namespace NUnitToXUnitConverter.Conversion
{
    public class NUnitFiles : IUnitTestsFiles
    {
        public File File { get; set; } = System.IO.InputOutput.Instance.File;
        public IProjectScanner ProjectScanner { get; set; } = new ProjectScanner();
        public IUnitTestDetector NUnitTestDetector { get; set; } = new NUnitTestDetector();

        public string[] GetNUnitCsFiles(string csprojPath)
        {
            return ProjectScanner.GetCsFiles(csprojPath).Where(NUnitTestDetector.IsNUnitTest).OrderBy(f => File.ReadAllText(f).Contains("[OneTimeSetUp]") ? "_____.cs" : f).ToArray();
        }
    }
}

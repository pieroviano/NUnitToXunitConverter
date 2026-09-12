using ConversionClassLibrary.Interfaces;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Matches a file written against either framework.
/// </summary>
/// <remarks>
/// A part-migrated project holding both is ordinary, and scanning with one detector or the other converted
/// whichever framework was found first while leaving every file of the other one untouched.
/// </remarks>
public class AnyFrameworkTestDetector : IUnitTestDetector
{
    public File File
    {
        get => NUnit.File;
        set
        {
            NUnit.File = value;
            MsTest.File = value;
        }
    }

    public IUnitTestDetector NUnit { get; set; } = new NUnitTestDetector();
    public IUnitTestDetector MsTest { get; set; } = new MsUnitTestDetector();

    public bool IsUnitTest(string file)
    {
        return NUnit.IsUnitTest(file) || MsTest.IsUnitTest(file);
    }
}

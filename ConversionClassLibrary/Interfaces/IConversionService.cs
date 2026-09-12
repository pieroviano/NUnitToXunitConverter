namespace ConversionClassLibrary.Interfaces;

/// <summary>Converts the test sources of a single project to xUnit, in place.</summary>
public interface IConversionService
{
    ConversionResult DoConversion(string csprojPath, bool forceMsTestProject);
}

namespace ConversionClassLibrary.Interfaces;

public interface IMsTestToNUnitContent
{
    string? Transform(string fileContent, bool format = true);
    string TransformMethods(string content);
    string FormatContent(string content);
}
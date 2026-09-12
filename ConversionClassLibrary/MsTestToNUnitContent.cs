using System.Text.RegularExpressions;
using ConversionClassLibrary.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace ConversionClassLibrary;

public class MsTestToNUnitContent : IMsTestToNUnitContent
{
    public string? Transform(string fileContent, bool format = true)
    {
        var original = fileContent;

        // Perform simple search and replace
        fileContent = fileContent.Replace("[TestClass]", "[TestFixture]")
            .Replace("[TestMethod]", "[Test]")
            .Replace("[TestInitialize]", "[SetUp]")
            .Replace("[TestCleanup]", "[TearDown]")
            .Replace("[TestClassInitialize]", "[TestFixtureSetUp]")
            .Replace("[TestClassCleanup]", "[TestFixtureTearDown]")
            .Replace("Assert.ThrowsException", "Assert.Throws")
            .Replace("using Microsoft.VisualStudio.TestTools.UnitTesting;", "using NUnit.Framework;");

        // Perform regex-powered search and replace
#pragma warning disable SYSLIB1045
        fileContent = Regex.Replace(fileContent, @"Assert.IsInstanceOfType\(([\w\[\]\.]+), typeof\((\w+)\)\);", "Assert.IsInstanceOf<$2>($1);");
#pragma warning restore SYSLIB1045

        // Write the modified content back to the file
        if (fileContent != original)
        {
            fileContent = TransformMethods(fileContent);
            if (format)
            {
                fileContent = FormatContent(fileContent);
            }
            return fileContent;
        }

        return null;
    }

    public string TransformMethods(string content)
    {
        // Define the regex pattern to find methods with the specified custom attribute
        var pattern = @"\[TestMethod, ExpectedException\(typeof\((\w+)Exception\)\)\]\s*public\s+void\s+(\w+)\(\)\s*\{((?>[^{}]+|(?<Open>{)|(?<-Open>}))+(?(Open)(?!)))\}";

        // Match methods with the specified custom attribute
        var matches = Regex.Matches(content, pattern);

        // Iterate through each match and perform the transformation
        foreach (Match match in matches)
        {
            var exceptionType = match.Groups[1].Value;
            var methodName = match.Groups[2].Value;
            var methodBody = match.Groups[3].Value;

            // Transform the method
            var transformedMethod = $"[Test]\npublic void {methodName}()\n{{\ntry\n{{\n{methodBody}\nAssert.Fail();\n}}\ncatch ({exceptionType}Exception e)\n{{\nConsole.WriteLine(e);\n}}\n}}";

            // Replace the original method with the transformed one
            content = content.Replace(match.Value, transformedMethod);
        }

        return content.Replace("\r\n", "\n");
    }

    public string FormatContent(string content)
    {
        try
        {
            var workspace = new AdhocWorkspace();
            var syntaxTree = CSharpSyntaxTree.ParseText(content);
            var root = syntaxTree.GetRoot();

            var formattedRoot = Formatter.Format(root, workspace);
            // Keep previous behavior of normalizing to '\n'
            return formattedRoot.ToFullString().Replace("\r\n", "\n");
        }
        catch (Exception)
        {
            // If Roslyn formatting fails for any reason, return original content unchanged
            return content;
        }
    }
}
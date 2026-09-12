using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectsLibrary.Conversion;

public enum Framework
{
    Unknown,
    NUnit,
    MsTest
}

/// <summary>
/// Works out which framework a file was written against.
/// </summary>
/// <remarks>
/// Needed because the two frameworks disagree on argument order for identically named members - NUnit's
/// <c>StringAssert.Contains(expected, actual)</c> against MSTest's <c>StringAssert.Contains(value, substring)</c>
/// - and the rewriter handles both in one pass, so the file itself has to say which it is.
/// </remarks>
public static class SourceFramework
{
    public static Framework Of(CompilationUnitSyntax node)
    {
        foreach (var name in node.Usings.Select(directive => directive.Name?.ToString()))
        {
            if (name == "NUnit.Framework")
                return Framework.NUnit;

            if (name == "Microsoft.VisualStudio.TestTools.UnitTesting")
                return Framework.MsTest;
        }

        // A file with no using of its own - global usings, or a nested partial - is placed by its attributes.
        var attributes = node.DescendantNodes()
            .OfType<AttributeSyntax>()
            .Select(attribute => attribute.Name.ToString())
            .ToList();

        if (attributes.Any(name => name is "TestFixture" or "Test" or "TestCase" or "SetUpFixture"))
            return Framework.NUnit;

        if (attributes.Any(name => name is "TestClass" or "TestMethod" or "DataTestMethod" or "DataRow"))
            return Framework.MsTest;

        return Framework.Unknown;
    }
}

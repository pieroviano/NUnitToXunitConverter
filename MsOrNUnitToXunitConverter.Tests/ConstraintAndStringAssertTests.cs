using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>Covers §3 of CONVERSION-GAPS.md: the constraint model and the companion assert classes.</summary>
public class ConstraintAndStringAssertTests
{
    private static string Rewrite(string framework, string body)
    {
        var code = $$"""
            using {{framework}};

            public class Tests
            {
                public void A()
                {
            {{body}}
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);

        return new XunitSyntaxRewriter().Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();
    }

    private static string NUnit(string body) => Rewrite("NUnit.Framework", body);

    private static string MsTest(string body) => Rewrite("Microsoft.VisualStudio.TestTools.UnitTesting", body);

    // ---------------- Assert.That ----------------

    [Theory]
    // NUnit reads actual-then-expected; every xUnit assert reads expected-then-actual.
    [InlineData("Assert.That(x, Is.EqualTo(1));", "Assert.Equal(1, x)")]
    [InlineData("Assert.That(x, Is.Not.EqualTo(1));", "Assert.NotEqual(1, x)")]
    [InlineData("Assert.That(x, Is.Null);", "Assert.Null(x)")]
    [InlineData("Assert.That(x, Is.Not.Null);", "Assert.NotNull(x)")]
    [InlineData("Assert.That(x, Is.True);", "Assert.True(x)")]
    [InlineData("Assert.That(x, Is.False);", "Assert.False(x)")]
    [InlineData("Assert.That(x, Is.Empty);", "Assert.Empty(x)")]
    [InlineData("Assert.That(x, Is.Not.Empty);", "Assert.NotEmpty(x)")]
    [InlineData("Assert.That(x, Is.SameAs(y));", "Assert.Same(y, x)")]
    [InlineData("Assert.That(x, Is.Not.SameAs(y));", "Assert.NotSame(y, x)")]
    [InlineData("Assert.That(x, Is.GreaterThan(1));", "Assert.True(x > 1)")]
    [InlineData("Assert.That(x, Is.GreaterThanOrEqualTo(1));", "Assert.True(x >= 1)")]
    [InlineData("Assert.That(x, Is.LessThan(1));", "Assert.True(x < 1)")]
    [InlineData("Assert.That(x, Is.LessThanOrEqualTo(1));", "Assert.True(x <= 1)")]
    [InlineData("Assert.That(x, Is.InRange(1, 5));", "Assert.InRange(x, 1, 5)")]
    [InlineData("Assert.That(x, Is.TypeOf<string>());", "Assert.IsType<string>(x)")]
    [InlineData("Assert.That(x, Is.InstanceOf<string>());", "Assert.IsAssignableFrom<string>(x)")]
    [InlineData("Assert.That(x, Does.Contain(\"a\"));", "Assert.Contains(\"a\", x)")]
    [InlineData("Assert.That(x, Does.Not.Contain(\"a\"));", "Assert.DoesNotContain(\"a\", x)")]
    [InlineData("Assert.That(x, Does.StartWith(\"a\"));", "Assert.StartsWith(\"a\", x)")]
    [InlineData("Assert.That(x, Does.EndWith(\"a\"));", "Assert.EndsWith(\"a\", x)")]
    [InlineData("Assert.That(x, Does.Match(\"a.c\"));", "Assert.Matches(\"a.c\", x)")]
    [InlineData("Assert.That(x, Contains.Item(1));", "Assert.Contains(1, x)")]
    public void Recognised_Constraints_Become_The_Matching_Assert(string source, string expected)
    {
        Assert.Contains(expected, NUnit($"        {source}"));
    }

    [Theory]
    [InlineData("Assert.That(() => Work(), Throws.TypeOf<InvalidOperationException>());",
        "Assert.Throws<InvalidOperationException>(() => Work())")]
    [InlineData("Assert.That(() => Work(), Throws.InstanceOf<InvalidOperationException>());",
        "Assert.ThrowsAny<InvalidOperationException>(() => Work())")]
    public void Throws_Constraints_Become_The_Throws_Asserts(string source, string expected)
    {
        Assert.Contains(expected, NUnit($"        {source}"));
    }

    [Theory]
    [InlineData("Assert.That(x, Has.Length.EqualTo(1));")]
    [InlineData("Assert.That(x, Is.EqualTo(1).Within(0.1));")]
    [InlineData("Assert.That(x, Is.Ordered);")]
    [InlineData("Assert.That(x, Is.Unique);")]
    public void An_Unrecognised_Constraint_Is_Left_Untouched(string source)
    {
        // A constraint is an open-ended expression tree. Guessing at one that is not understood would be
        // worse than leaving it for a human.
        Assert.Contains(source.TrimEnd(';'), NUnit($"        {source}"));
    }

    [Fact]
    public void A_Constraint_With_A_Message_Still_Reaches_The_Output_Helper()
    {
        var result = NUnit("        Assert.That(x, Is.EqualTo(1), \"they differ\");");

        Assert.Contains("Assert.Equal(1, x)", result);
        Assert.Contains("_output.WriteLine(\"they differ\")", result);
    }

    // ---------------- StringAssert ----------------

    [Theory]
    // NUnit takes (expected, actual): the needle is already first, as xUnit wants it.
    [InlineData("StringAssert.Contains(\"a\", text);", "Assert.Contains(\"a\", text)")]
    [InlineData("StringAssert.DoesNotContain(\"a\", text);", "Assert.DoesNotContain(\"a\", text)")]
    [InlineData("StringAssert.StartsWith(\"a\", text);", "Assert.StartsWith(\"a\", text)")]
    [InlineData("StringAssert.EndsWith(\"a\", text);", "Assert.EndsWith(\"a\", text)")]
    [InlineData("StringAssert.IsMatch(\"a.c\", text);", "Assert.Matches(\"a.c\", text)")]
    [InlineData("StringAssert.AreEqualIgnoringCase(\"a\", text);", "Assert.Equal(\"a\", text, ignoreCase: true)")]
    public void NUnit_StringAssert_Keeps_Its_Argument_Order(string source, string expected)
    {
        Assert.Contains(expected, NUnit($"        {source}"));
    }

    [Theory]
    // MSTest takes (value, substring): the needle is second and has to be swapped to the front.
    [InlineData("StringAssert.Contains(text, \"a\");", "Assert.Contains(\"a\", text)")]
    [InlineData("StringAssert.StartsWith(text, \"a\");", "Assert.StartsWith(\"a\", text)")]
    [InlineData("StringAssert.EndsWith(text, \"a\");", "Assert.EndsWith(\"a\", text)")]
    [InlineData("StringAssert.Matches(text, pattern);", "Assert.Matches(pattern, text)")]
    [InlineData("StringAssert.DoesNotMatch(text, pattern);", "Assert.DoesNotMatch(pattern, text)")]
    public void MsTest_StringAssert_Has_Its_Arguments_Swapped(string source, string expected)
    {
        Assert.Contains(expected, MsTest($"        {source}"));
    }

    [Fact]
    public void StringAssert_Is_Left_Alone_When_The_Framework_Is_Unknown()
    {
        // Same member names, opposite argument order: guessing reverses the meaning of the assert silently.
        var code = """
            public class Tests
            {
                public void A()
                {
                    StringAssert.Contains(text, "a");
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var result = new XunitSyntaxRewriter().Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();

        Assert.Contains("StringAssert.Contains(text, \"a\")", result);
    }

    [Fact]
    public void The_NUnit_Only_Directory_And_File_Asserts_Are_Left_Untouched()
    {
        var result = NUnit("""
                    DirectoryAssert.Exists(path);
                    FileAssert.Exists(path);
            """);

        Assert.Contains("DirectoryAssert.Exists(path)", result);
        Assert.Contains("FileAssert.Exists(path)", result);
    }
}

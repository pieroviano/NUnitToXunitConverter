using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

public class ExpectedExceptionRewriteTests
{
    private static string Rewrite(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var rewritten = new XunitSyntaxRewriter().Visit(tree.GetRoot());

        return rewritten!.NormalizeWhitespace().ToFullString();
    }

    [Fact]
    public void ExpectedException_On_Async_Method_Becomes_Awaited_ThrowsAsync()
    {
        var result = Rewrite(@"
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class Tests
{
    [TestMethod]
    [ExpectedException(typeof(NotSupportedException))]
    public async Task ThrowsOnNoUnconditionalPropertyGroup()
    {
        var xml = ""<Project/>"";

        await ParseAndTransform(xml).ConfigureAwait(false);
    }
}");

        Assert.Contains("[Fact]", result);
        Assert.DoesNotContain("ExpectedException", result);
        Assert.DoesNotContain("TestMethod", result);
        Assert.DoesNotContain("TestClass", result);
        Assert.Contains("await Assert.ThrowsAsync<NotSupportedException>(async () =>", result);
        // The body moves across untouched, awaits and all.
        Assert.Contains("await ParseAndTransform(xml).ConfigureAwait(false);", result);
        Assert.Contains("public async Task ThrowsOnNoUnconditionalPropertyGroup()", result);
    }

    [Fact]
    public void ExpectedException_On_Sync_Method_Becomes_Throws()
    {
        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestMethod, ExpectedException(typeof(InvalidOperationException))]
    public void Blows()
    {
        Parse(""bad"");
    }
}");

        Assert.Contains("[Fact]", result);
        Assert.Contains("Assert.Throws<InvalidOperationException>(() =>", result);
        Assert.DoesNotContain("await", result);
        Assert.Contains(@"Parse(""bad"");", result);
    }

    [Fact]
    public void Unawaited_ThrowsAsync_Gets_Awaited()
    {
        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestMethod]
    public async Task ThrowsOnNoUnconditionalPropertyGroup()
    {
        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await ParseAndTransform(""<Project/>"").ConfigureAwait(false);
        });
    }
}");

        Assert.Contains("await Assert.ThrowsAsync<NotSupportedException>(async () =>", result);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "Assert.ThrowsAsync"));
    }

    [Fact]
    public void MsTest_ThrowsExceptionAsync_Is_Renamed_And_Awaited_Keeping_Type_Argument()
    {
        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestMethod]
    public async Task Blows()
    {
        Assert.ThrowsExceptionAsync<NotSupportedException>(() => Work());
    }
}");

        Assert.Contains("await Assert.ThrowsAsync<NotSupportedException>(", result);
        Assert.DoesNotContain("ThrowsExceptionAsync", result);
    }

    [Fact]
    public void Verbatim_String_Bodies_Are_Preserved_Byte_For_Byte()
    {
        const string xmlLiteral = "@\"<Project>\n <OutputType>Library</OutputType>\n</Project>\"";

        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestMethod]
    [ExpectedException(typeof(NotSupportedException))]
    public async Task Throws()
    {
        var xml = " + xmlLiteral + @";
        await ParseAndTransform(xml);
    }
}");

        // Reformatting must not reach inside a verbatim literal - it is the test's data.
        Assert.Contains(xmlLiteral, result);
    }

    [Fact]
    public void MsTest_Initialize_And_Cleanup_Become_Constructor_And_Dispose()
    {
        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestInitialize]
    public void Setup() { Prepare(); }

    [TestCleanup]
    public void Cleanup() { Release(); }

    [TestMethod]
    public void Works() { }
}");

        Assert.Contains("public Tests()", result);
        Assert.Contains("Prepare();", result);
        Assert.Contains("public void Dispose()", result);
        Assert.Contains("Release();", result);
        Assert.Contains("System.IDisposable", result);
    }

    [Fact]
    public void MsTest_DataRow_Becomes_InlineData_And_IsInstanceOfType_Becomes_IsType()
    {
        var result = Rewrite(@"
[TestClass]
public class Tests
{
    [TestMethod]
    [DataRow(1)]
    public void Works(int value)
    {
        Assert.IsInstanceOfType(value, typeof(int));
    }
}");

        Assert.Contains("[InlineData(1)]", result);
        Assert.Contains("Assert.IsType<int>(value)", result);
    }

    [Fact]
    public void MsTest_Using_Becomes_Xunit()
    {
        var result = Rewrite(@"
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class Tests
{
    [TestMethod]
    public void Works() { }
}");

        Assert.Contains("using Xunit;", result);
        Assert.DoesNotContain("Microsoft.VisualStudio.TestTools.UnitTesting", result);
    }
}

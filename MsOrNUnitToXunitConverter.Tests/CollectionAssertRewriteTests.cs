using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

public class CollectionAssertRewriteTests
{
    private static string Rewrite(string body)
    {
        var code = @"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
" + body + @"
    }
}";

        var tree = CSharpSyntaxTree.ParseText(code);
        var rewritten = new XunitSyntaxRewriter().Visit(tree.GetRoot());

        return rewritten!.NormalizeWhitespace().ToFullString();
    }

    [Fact]
    public void AreEqual_And_AreNotEqual_Become_Equal_And_NotEqual()
    {
        var result = Rewrite(@"
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreNotEqual(expected, actual);");

        Assert.Contains("Assert.Equal(expected, actual)", result);
        Assert.Contains("Assert.NotEqual(expected, actual)", result);
        Assert.DoesNotContain("CollectionAssert", result);
    }

    [Fact]
    public void AreEquivalent_Becomes_Equivalent()
    {
        var result = Rewrite("CollectionAssert.AreEquivalent(expected, actual);");

        Assert.Contains("Assert.Equivalent(expected, actual)", result);
    }

    [Fact]
    public void Contains_And_DoesNotContain_Swap_Collection_And_Element()
    {
        var result = Rewrite(@"
        CollectionAssert.Contains(items, needle);
        CollectionAssert.DoesNotContain(items, needle);");

        Assert.Contains("Assert.Contains(needle, items)", result);
        Assert.Contains("Assert.DoesNotContain(needle, items)", result);
    }

    [Fact]
    public void IsEmpty_And_IsNotEmpty_Become_Empty_And_NotEmpty()
    {
        var result = Rewrite(@"
        CollectionAssert.IsEmpty(items);
        CollectionAssert.IsNotEmpty(others);");

        Assert.Contains("Assert.Empty(items)", result);
        Assert.Contains("Assert.NotEmpty(others)", result);
    }

    [Fact]
    public void AllItemsAreNotNull_Becomes_Assert_All()
    {
        var result = Rewrite("CollectionAssert.AllItemsAreNotNull(items);");

        Assert.Contains("Assert.All(items, item => Assert.NotNull(item))", result);
    }

    [Fact]
    public void AllItemsAreInstancesOfType_Lifts_The_Type_Into_A_Type_Argument()
    {
        var result = Rewrite("CollectionAssert.AllItemsAreInstancesOfType(items, typeof(Widget));");

        Assert.Contains("Assert.All(items, item => Assert.IsType<Widget>(item))", result);
    }

    [Fact]
    public void AllItemsAreInstancesOfType_With_A_Type_Expression_Is_Left_Untouched()
    {
        // Nothing to lift into a type argument, so the call has to stay and fail to compile.
        var result = Rewrite("CollectionAssert.AllItemsAreInstancesOfType(items, expectedType);");

        Assert.Contains("CollectionAssert.AllItemsAreInstancesOfType(items, expectedType)", result);
    }

    [Fact]
    public void IsSubsetOf_Becomes_Assert_All_Over_Contains()
    {
        var result = Rewrite("CollectionAssert.IsSubsetOf(subset, superset);");

        Assert.Contains("Assert.All(subset, item => Assert.Contains(item, superset))", result);
    }

    [Fact]
    public void AllItemsAreUnique_Compares_Distinct_Count_And_Adds_The_Linq_Using()
    {
        var result = Rewrite("CollectionAssert.AllItemsAreUnique(items);");

        Assert.Contains("Assert.Equal(items.Distinct().Count(), items.Count())", result);
        Assert.Contains("using System.Linq;", result);
    }

    [Fact]
    public void The_Linq_Using_Is_Not_Added_Twice()
    {
        var code = @"
using System.Linq;
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
        CollectionAssert.AllItemsAreUnique(items);
    }
}";

        var tree = CSharpSyntaxTree.ParseText(code);
        var result = new XunitSyntaxRewriter().Visit(tree.GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();

        Assert.Equal(1, result.Split("using System.Linq;").Length - 1);
    }

    [Fact]
    public void The_Linq_Using_Is_Only_Added_When_Something_Needs_It()
    {
        var result = Rewrite("CollectionAssert.AreEqual(expected, actual);");

        Assert.DoesNotContain("using System.Linq;", result);
    }

    [Fact]
    public void A_Failure_Message_Is_Written_To_The_Output_Helper()
    {
        var result = Rewrite(@"CollectionAssert.AreEqual(expected, actual, ""they differ"");");

        Assert.Contains("try", result);
        Assert.Contains("Assert.Equal(expected, actual)", result);
        Assert.Contains(@"_output.WriteLine(""they differ"")", result);
        Assert.Contains("throw;", result);
        // The wrap pulls in the ITestOutputHelper field and constructor parameter.
        Assert.Contains("private readonly ITestOutputHelper _output", result);
        Assert.Contains("public Tests(ITestOutputHelper output)", result);
    }

    [Fact]
    public void A_Failure_Message_Keeps_Its_Format_Arguments()
    {
        var result = Rewrite(@"CollectionAssert.IsEmpty(items, ""still holds {0}"", items.Count);");

        Assert.Contains("Assert.Empty(items)", result);
        Assert.Contains(@"_output.WriteLine(""still holds {0}"", items.Count)", result);
    }

    [Fact]
    public void Members_Without_An_Xunit_Counterpart_Are_Left_Untouched()
    {
        var result = Rewrite(@"
        CollectionAssert.AreNotEquivalent(expected, actual);
        CollectionAssert.IsNotSubsetOf(subset, superset);
        CollectionAssert.IsOrdered(items);");

        Assert.Contains("CollectionAssert.AreNotEquivalent(expected, actual)", result);
        Assert.Contains("CollectionAssert.IsNotSubsetOf(subset, superset)", result);
        Assert.Contains("CollectionAssert.IsOrdered(items)", result);
    }

    [Fact]
    public void MsTest_CollectionAssert_Is_Converted_In_The_Same_Pass()
    {
        var code = @"
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class Tests
{
    [TestMethod]
    public void Check()
    {
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.Contains(items, needle);
    }
}";

        var tree = CSharpSyntaxTree.ParseText(code);
        var result = new XunitSyntaxRewriter().Visit(tree.GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();

        Assert.Contains("using Xunit;", result);
        Assert.Contains("Assert.Equal(expected, actual)", result);
        Assert.Contains("Assert.Contains(needle, items)", result);
    }

    [Fact]
    public void A_Plain_Assert_Keeps_Its_Existing_Message_Behaviour()
    {
        var result = Rewrite(@"Assert.AreEqual(1, 1, ""failed"");");

        Assert.Contains("Assert.Equal(1, 1)", result);
        Assert.Contains(@"_output.WriteLine(""failed"")", result);
    }
}

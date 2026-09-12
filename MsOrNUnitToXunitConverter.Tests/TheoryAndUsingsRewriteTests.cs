using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

public class TheoryAndUsingsRewriteTests
{
    private static string Rewrite(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);

        return new XunitSyntaxRewriter().Visit(tree.GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();
    }

    [Fact]
    public void NUnit_Test_With_TestCase_Becomes_Theory_With_InlineData()
    {
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    [TestCase(1)]
    [TestCase(2)]
    public void Check(int value) { }
}");

        Assert.Contains("[Theory]", result);
        Assert.Contains("[InlineData(1)]", result);
        Assert.Contains("[InlineData(2)]", result);
        Assert.DoesNotContain("[Fact]", result);
    }

    [Fact]
    public void NUnit_TestCase_Without_Test_Gets_A_Theory_Added()
    {
        // [TestCase] stands on its own in NUnit, so there is no [Fact] to rename.
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [TestCase(1)]
    public void Check(int value) { }
}");

        Assert.Contains("[Theory]", result);
        Assert.Contains("[InlineData(1)]", result);
    }

    [Fact]
    public void MsTest_TestMethod_With_DataRow_Becomes_Theory_With_InlineData()
    {
        var result = Rewrite(@"
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class Tests
{
    [TestMethod]
    [DataRow(1, 2)]
    public void Check(int a, int b) { }
}");

        Assert.Contains("[Theory]", result);
        Assert.Contains("[InlineData(1, 2)]", result);
        Assert.DoesNotContain("[Fact]", result);
    }

    [Fact]
    public void DataRow_And_TestMethod_Sharing_One_Attribute_List_Still_Become_Theory()
    {
        var result = Rewrite(@"
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class Tests
{
    [TestMethod, DataRow(1)]
    public void Check(int value) { }
}");

        Assert.Contains("Theory", result);
        Assert.Contains("InlineData(1)", result);
        Assert.DoesNotContain("Fact", result);
    }

    [Fact]
    public void A_Test_Without_Data_Rows_Stays_A_Fact()
    {
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check() { }
}");

        Assert.Contains("[Fact]", result);
        Assert.DoesNotContain("Theory", result);
    }

    [Fact]
    public void The_Output_Helper_Brings_In_The_Xunit_Abstractions_Using()
    {
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
        Assert.AreEqual(1, 2, ""they differ"");
    }
}");

        Assert.Contains("using Xunit;", result);
        Assert.Contains("using Xunit.Abstractions;", result);
        Assert.Contains("private readonly ITestOutputHelper _output", result);
    }

    [Fact]
    public void CollectionAssert_Messages_Bring_In_The_Xunit_Abstractions_Using_Too()
    {
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
        CollectionAssert.IsEmpty(items, ""still holds"");
    }
}");

        Assert.Contains("using Xunit.Abstractions;", result);
    }

    [Fact]
    public void The_Xunit_Abstractions_Using_Is_Not_Added_Twice()
    {
        var result = Rewrite(@"
using NUnit.Framework;
using Xunit.Abstractions;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
        Assert.AreEqual(1, 2, ""they differ"");
    }
}");

        Assert.Equal(1, result.Split("using Xunit.Abstractions;").Length - 1);
    }

    [Fact]
    public void A_File_That_Needs_No_Output_Helper_Does_Not_Get_The_Using()
    {
        var result = Rewrite(@"
using NUnit.Framework;

[TestFixture]
public class Tests
{
    [Test]
    public void Check()
    {
        Assert.AreEqual(1, 2);
    }
}");

        Assert.DoesNotContain("Xunit.Abstractions", result);
    }
}

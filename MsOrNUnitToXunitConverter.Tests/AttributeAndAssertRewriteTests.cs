using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>Covers §1.3, §1.5, §2 and §3 of CONVERSION-GAPS.md.</summary>
public class AttributeAndAssertRewriteTests
{
    private static string Rewrite(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);

        return new XunitSyntaxRewriter().Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();
    }

    private static string InNUnitTest(string members)
    {
        return Rewrite($$"""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
            {{members}}
            }
            """);
    }

    private static string InMsTestTest(string members)
    {
        return Rewrite($$"""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
            {{members}}
            }
            """);
    }

    // ---------------- skip ----------------

    [Fact]
    public void NUnit_Ignore_Becomes_A_Skip_Reason()
    {
        var result = InNUnitTest("""
                [Test, Ignore("flaky")]
                public void A() { }
            """);

        Assert.Contains("[Fact(Skip = \"flaky\")]", result);
    }

    [Fact]
    public void MsTest_Ignore_Without_A_Reason_Still_Skips()
    {
        var result = InMsTestTest("""
                [TestMethod, Ignore]
                public void A() { }
            """);

        Assert.Contains("[Fact(Skip = \"Ignored\")]", result);
    }

    [Fact]
    public void Explicit_Becomes_A_Skip()
    {
        var result = InNUnitTest("""
                [Test, Explicit]
                public void A() { }
            """);

        Assert.Contains("[Fact(Skip = \"Explicit\")]", result);
    }

    [Fact]
    public void Timeout_Becomes_A_Fact_Argument()
    {
        var result = InNUnitTest("""
                [Test, Timeout(500)]
                public void A() { }
            """);

        Assert.Contains("[Fact(Timeout = 500)]", result);
    }

    // ---------------- traits ----------------

    [Theory]
    [InlineData("[Test, Category(\"slow\")]", "[Trait(\"Category\", \"slow\")]")]
    [InlineData("[Test, Description(\"what\")]", "[Trait(\"Description\", \"what\")]")]
    [InlineData("[Test, Author(\"me\")]", "[Trait(\"Author\", \"me\")]")]
    [InlineData("[Test, Property(\"k\", \"v\")]", "[Trait(\"k\", \"v\")]")]
    public void NUnit_Annotations_Become_Traits(string attributes, string expected)
    {
        var result = InNUnitTest($$"""
                {{attributes}}
                public void A() { }
            """);

        Assert.Contains(expected, result);
    }

    [Theory]
    [InlineData("[TestMethod, TestCategory(\"slow\")]", "[Trait(\"Category\", \"slow\")]")]
    [InlineData("[TestMethod, Owner(\"me\")]", "[Trait(\"Owner\", \"me\")]")]
    [InlineData("[TestMethod, TestProperty(\"k\", \"v\")]", "[Trait(\"k\", \"v\")]")]
    public void MsTest_Annotations_Become_Traits(string attributes, string expected)
    {
        var result = InMsTestTest($$"""
                {{attributes}}
                public void A() { }
            """);

        Assert.Contains(expected, result);
    }

    [Fact]
    public void A_Non_String_Trait_Value_Is_Quoted()
    {
        // xUnit traits are compile-time constant strings, so [Priority(1)] cannot pass 1 through.
        var result = InMsTestTest("""
                [TestMethod, Priority(1)]
                public void A() { }
            """);

        Assert.Contains("[Trait(\"Priority\", \"1\")]", result);
    }

    [Fact]
    public void A_Category_On_The_Class_Becomes_A_Class_Trait()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            [Category("fast")]
            public class Tests
            {
                [Test]
                public void A() { }
            }
            """);

        Assert.Contains("[Trait(\"Category\", \"fast\")]", result);
    }

    // ---------------- data sources ----------------

    [Fact]
    public void TestCaseSource_Becomes_MemberData_On_A_Theory()
    {
        var result = InNUnitTest("""
                [TestCaseSource(nameof(Cases))]
                public void A(int value) { }
            """);

        Assert.Contains("[Theory]", result);
        Assert.Contains("[MemberData(nameof(Cases))]", result);
        Assert.DoesNotContain("TestCaseSource", result);
    }

    [Fact]
    public void DynamicData_Becomes_MemberData_On_A_Theory()
    {
        var result = InMsTestTest("""
                [TestMethod]
                [DynamicData(nameof(Cases))]
                public void A(int value) { }
            """);

        Assert.Contains("[Theory]", result);
        Assert.Contains("[MemberData(nameof(Cases))]", result);
        Assert.DoesNotContain("[Fact]", result);
    }

    [Fact]
    public void A_Source_Declaring_Type_Becomes_MemberType()
    {
        var result = InNUnitTest("""
                [TestCaseSource(typeof(Data), nameof(Data.Cases))]
                public void A(int value) { }
            """);

        Assert.Contains("MemberType = typeof(Data)", result);
    }

    [Fact]
    public void DataTestMethod_Is_Dropped_In_Favour_Of_Theory()
    {
        var result = InMsTestTest("""
                [DataTestMethod]
                [DataRow(1)]
                public void A(int value) { }
            """);

        Assert.Contains("[Theory]", result);
        Assert.Contains("[InlineData(1)]", result);
        Assert.DoesNotContain("DataTestMethod", result);
    }

    // ---------------- ExpectedResult ----------------

    [Fact]
    public void An_ExpectedResult_Becomes_A_Parameter_And_An_Assert()
    {
        var result = InNUnitTest("""
                [TestCase(1, ExpectedResult = 2)]
                public int A(int a) => a + 1;
            """);

        Assert.Contains("[InlineData(1, 2)]", result);
        Assert.Contains("public void A(int a, int expectedResult)", result);
        Assert.Contains("Assert.Equal(expectedResult, a + 1);", result);
        Assert.DoesNotContain("ExpectedResult", result);
    }

    [Fact]
    public void An_ExpectedResult_On_A_Block_Body_Converts_Its_Returns()
    {
        var result = InNUnitTest("""
                [TestCase(1, ExpectedResult = 2)]
                public int A(int a)
                {
                    return a + 1;
                }
            """);

        Assert.Contains("Assert.Equal(expectedResult, a + 1);", result);
        Assert.Contains("public void A(int a, int expectedResult)", result);
    }

    // ---------------- the delta overload ----------------

    [Fact]
    public void A_Power_Of_Ten_Delta_Becomes_A_Precision()
    {
        var result = InNUnitTest("""
                [Test]
                public void A() { Assert.AreEqual(1.0, 2.0, 0.001); }
            """);

        // xUnit compares to a number of decimal places, not to a tolerance.
        Assert.Contains("Assert.Equal(1.0, 2.0, 3)", result);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("delta")]
    public void A_Delta_That_Cannot_Be_Expressed_Exactly_Is_Left_Untouched(string delta)
    {
        var result = InNUnitTest($$"""
                [Test]
                public void A() { Assert.AreEqual(1.0, 2.0, {{delta}}); }
            """);

        // Silently giving it a different tolerance, or writing it to the output helper, are both worse.
        Assert.Contains($"Assert.AreEqual(1.0, 2.0, {delta})", result);
        Assert.DoesNotContain("_output", result);
    }

    [Fact]
    public void A_Real_Message_Still_Reaches_The_Output_Helper()
    {
        var result = InNUnitTest("""
                [Test]
                public void A() { Assert.AreEqual(1, 2, "they differ"); }
            """);

        Assert.Contains("Assert.Equal(1, 2)", result);
        Assert.Contains("_output.WriteLine(\"they differ\")", result);
    }

    // ---------------- classic asserts ----------------

    [Theory]
    [InlineData("Assert.Greater(2, 1);", "Assert.True(2 > 1)")]
    [InlineData("Assert.GreaterOrEqual(2, 1);", "Assert.True(2 >= 1)")]
    [InlineData("Assert.Less(1, 2);", "Assert.True(1 < 2)")]
    [InlineData("Assert.LessOrEqual(1, 2);", "Assert.True(1 <= 2)")]
    [InlineData("Assert.Positive(x);", "Assert.True(x > 0)")]
    [InlineData("Assert.Negative(x);", "Assert.True(x < 0)")]
    [InlineData("Assert.Zero(x);", "Assert.Equal(0, x)")]
    [InlineData("Assert.NotZero(x);", "Assert.NotEqual(0, x)")]
    [InlineData("Assert.IsEmpty(x);", "Assert.Empty(x)")]
    [InlineData("Assert.IsNotEmpty(x);", "Assert.NotEmpty(x)")]
    public void Comparison_And_Sign_Asserts_Are_Spelled_Out(string source, string expected)
    {
        var result = InNUnitTest($$"""
                [Test]
                public void A() { {{source}} }
            """);

        Assert.Contains(expected, result);
    }

    [Fact]
    public void MsTest_IsNotInstanceOfType_Becomes_IsNotType()
    {
        var result = InMsTestTest("""
                [TestMethod]
                public void A() { Assert.IsNotInstanceOfType(x, typeof(string)); }
            """);

        Assert.Contains("Assert.IsNotType<string>(x)", result);
    }

    [Fact]
    public void Asserts_Without_An_Xunit_Counterpart_Are_Left_Untouched()
    {
        var result = InNUnitTest("""
                [Test]
                public void A()
                {
                    Assert.Pass("done");
                    Assert.Inconclusive("dunno");
                    Assert.Ignore("skip");
                    Assert.Warn("careful");
                    Assert.Multiple(() => { });
                }
            """);

        Assert.Contains("Assert.Pass(\"done\")", result);
        Assert.Contains("Assert.Inconclusive(\"dunno\")", result);
        Assert.Contains("Assert.Ignore(\"skip\")", result);
        Assert.Contains("Assert.Warn(\"careful\")", result);
        Assert.Contains("Assert.Multiple(", result);
    }

    [Fact]
    public void Attributes_Without_An_Xunit_Counterpart_Are_Left_Untouched()
    {
        var result = InNUnitTest("""
                [Test, Order(3), Retry(2), Repeat(5), MaxTime(100)]
                public void A() { }
            """);

        Assert.Contains("Order(3)", result);
        Assert.Contains("Retry(2)", result);
        Assert.Contains("Repeat(5)", result);
        Assert.Contains("MaxTime(100)", result);
    }
}

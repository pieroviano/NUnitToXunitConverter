using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>Covers §1.1, §1.2 and the fixture generation of CONVERSION-GAPS.md.</summary>
public class LifecycleRewriteTests
{
    private static string Rewrite(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);

        return new XunitSyntaxRewriter().Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();
    }

    [Fact]
    public void A_SetUp_Method_Is_Moved_Into_The_Constructor_Not_Copied()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [SetUp]
                public void Before() { Prepare(); }

                [Test]
                public void A() { }

                private void Prepare() { }
            }
            """);

        Assert.Contains("public Tests()", result);
        // The body moved; leaving the original behind meant it ran twice and did not compile.
        Assert.DoesNotContain("[SetUp]", result);
        Assert.DoesNotContain("public void Before()", result);
        Assert.Equal(1, result.Split("Prepare();").Length - 1);
    }

    [Fact]
    public void A_TearDown_Method_Is_Moved_Into_Dispose_Not_Copied()
    {
        var result = Rewrite("""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                [TestCleanup]
                public void After() { Release(); }

                [TestMethod]
                public void A() { }

                private void Release() { }
            }
            """);

        Assert.Contains("System.IDisposable", result);
        Assert.Contains("public void Dispose()", result);
        Assert.DoesNotContain("[TestCleanup]", result);
        Assert.DoesNotContain("public void After()", result);
        Assert.Equal(1, result.Split("Release();").Length - 1);
    }

    [Fact]
    public void Setup_Of_One_Class_Does_Not_Leak_Into_The_Next_In_The_Same_File()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            public class First
            {
                [SetUp]
                public void Before() { Prepare(); }

                [Test]
                public void A() { }

                private void Prepare() { }
            }

            [TestFixture]
            public class Second
            {
                [Test]
                public void B() { }
            }
            """);

        var second = result[result.IndexOf("class Second", StringComparison.Ordinal)..];

        // Second has no setup of its own, so it must gain neither a constructor nor First's body.
        Assert.DoesNotContain("Prepare();", second);
        Assert.DoesNotContain("public Second(", second);
        Assert.DoesNotContain("IDisposable", second);
    }

    [Fact]
    public void An_Assert_Inside_A_SetUp_Body_Is_Converted_Too()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [SetUp]
                public void Before() { Assert.AreEqual(1, 1); }

                [Test]
                public void A() { }
            }
            """);

        // The body is taken from the rewritten method, not the original.
        Assert.Contains("Assert.Equal(1, 1);", result);
        Assert.DoesNotContain("AreEqual", result);
    }

    [Fact]
    public void OneTimeTearDown_Becomes_Dispose_On_The_Class_Fixture()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [OneTimeSetUp]
                public void Once() { Open(); }

                [OneTimeTearDown]
                public void AfterAll() { Close(); }

                [Test]
                public void A() { }

                private void Open() { }
                private void Close() { }
            }
            """);

        Assert.Contains("IClassFixture<TestsFixture>", result);
        Assert.Contains("class TestsFixture : System.IDisposable", result);
        Assert.Contains("public TestsFixture()", result);
        Assert.Contains("Open();", result);
        Assert.Contains("Close();", result);
        Assert.DoesNotContain("[OneTimeTearDown]", result);
    }

    [Fact]
    public void MsTest_Class_Hooks_Become_The_Class_Fixture()
    {
        var result = Rewrite("""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                [ClassInitialize]
                public static void Init(TestContext context) { Open(); }

                [ClassCleanup]
                public static void Clean() { Close(); }

                [TestMethod]
                public void A() { }

                private static void Open() { }
                private static void Close() { }
            }
            """);

        Assert.Contains("IClassFixture<TestsFixture>", result);
        Assert.Contains("Open();", result);
        Assert.Contains("Close();", result);
        Assert.DoesNotContain("[ClassInitialize]", result);
        Assert.DoesNotContain("[ClassCleanup]", result);
    }

    [Fact]
    public void A_Fixture_Generated_For_A_Block_Namespace_Goes_Inside_It()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            namespace Tests
            {
                [TestFixture]
                public class Suite
                {
                    [OneTimeSetUp]
                    public void Once() { }

                    [Test]
                    public void A() { }
                }
            }
            """);

        // Appended to the compilation unit it would land in the global namespace, where the
        // IClassFixture<SuiteFixture> on the class could not resolve it.
        var namespaceEnd = result.LastIndexOf('}');
        var fixture = result.IndexOf("class SuiteFixture", StringComparison.Ordinal);

        Assert.True(fixture > 0 && fixture < namespaceEnd);
    }

    [Fact]
    public void A_SetUpFixture_Becomes_An_Assembly_Collection_Not_A_Class_Fixture()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [SetUpFixture]
            public class AssemblyLevel
            {
                [OneTimeSetUp]
                public void Once() { Open(); }

                [OneTimeTearDown]
                public void Teardown() { Close(); }

                private void Open() { }
                private void Close() { }
            }
            """);

        // [SetUpFixture] is assembly-wide; an IClassFixture would silently narrow it to one class.
        Assert.Contains("[CollectionDefinition(\"Assembly\")]", result);
        Assert.Contains("class AssemblyCollection : ICollectionFixture<AssemblyFixture>", result);
        Assert.Contains("class AssemblyFixture : System.IDisposable", result);
        Assert.Contains("Open();", result);
        Assert.Contains("Close();", result);
        Assert.DoesNotContain("IClassFixture", result);
    }

    [Fact]
    public void A_Parameterised_TestFixture_Is_Left_Untouched_Rather_Than_Silently_Dropped()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture(1)]
            [TestFixture(2)]
            public class Tests
            {
                public Tests(int value) { }

                [Test]
                public void A() { }
            }
            """);

        // Dropping these lost the two parameterisations without a word; xUnit has no equivalent, so the
        // attribute stays and the compiler raises it.
        Assert.Contains("[TestFixture(1)]", result);
        Assert.Contains("[TestFixture(2)]", result);
    }

    [Fact]
    public void A_Plain_TestFixture_Is_Still_Dropped()
    {
        var result = Rewrite("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [Test]
                public void A() { }
            }
            """);

        Assert.DoesNotContain("TestFixture", result);
    }

    [Fact]
    public void The_MsTest_TestContext_Property_And_Its_Writes_Become_The_Output_Helper()
    {
        var result = Rewrite("""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                public TestContext TestContext { get; set; }

                [TestMethod]
                public void A() { TestContext.WriteLine("hello"); }
            }
            """);

        Assert.DoesNotContain("TestContext", result);
        Assert.Contains("_output.WriteLine(\"hello\")", result);
        Assert.Contains("private readonly ITestOutputHelper _output", result);
        Assert.Contains("using Xunit.Abstractions;", result);
    }
}

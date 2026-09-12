using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>Covers the §4 items of CONVERSION-GAPS.md that have an xUnit counterpart.</summary>
public class AssemblySettingsRewriteTests
{
    private static string Rewrite(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);

        return new XunitSyntaxRewriter().Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();
    }

    [Fact]
    public void An_MsTest_Settings_File_Is_Selected_By_The_Detector()
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("MSTestSettings.cs")
            .Returns("[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]");

        // It holds no test, so nothing selected it and it was left referencing MSTest after the package
        // swap - the converted project then would not compile until the file was deleted by hand.
        Assert.True(new MsUnitTestDetector { File = file }.IsUnitTest("MSTestSettings.cs"));
    }

    [Fact]
    public void An_NUnit_Settings_File_Is_Selected_Too()
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("Settings.cs").Returns("[assembly: LevelOfParallelism(4)]");

        Assert.True(new NUnitTestDetector { File = file }.IsUnitTest("Settings.cs"));
    }

    [Fact]
    public void Parallelize_Has_No_Counterpart_And_Is_Dropped()
    {
        var result = Rewrite("[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]");

        // xUnit parallelises by collection; there is no method-level equivalent to carry over.
        Assert.DoesNotContain("Parallelize", result);
        Assert.DoesNotContain("CollectionBehavior", result);
    }

    [Fact]
    public void DoNotParallelize_Becomes_CollectionBehavior()
    {
        var result = Rewrite("[assembly: DoNotParallelize]");

        Assert.Contains("[assembly: CollectionBehavior(DisableTestParallelization = true)]", result);
        Assert.Contains("using Xunit;", result);
    }

    [Fact]
    public void NUnit_ParallelScope_None_Becomes_CollectionBehavior()
    {
        var result = Rewrite("[assembly: Parallelizable(ParallelScope.None)]");

        Assert.Contains("DisableTestParallelization = true", result);
    }

    [Fact]
    public void LevelOfParallelism_Becomes_MaxParallelThreads()
    {
        var result = Rewrite("[assembly: LevelOfParallelism(4)]");

        Assert.Contains("[assembly: CollectionBehavior(MaxParallelThreads = 4)]", result);
    }

    [Fact]
    public void ExpectedException_With_AllowDerivedTypes_Becomes_ThrowsAny()
    {
        var result = Rewrite("""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                [TestMethod]
                [ExpectedException(typeof(System.Exception), AllowDerivedTypes = true)]
                public void A() { Work(); }

                private void Work() { }
            }
            """);

        // Assert.Throws<T> demands that exact type; only ThrowsAny<T> accepts a subclass.
        Assert.Contains("Assert.ThrowsAny<System.Exception>(", result);
    }

    [Fact]
    public void ExpectedException_Without_It_Still_Demands_The_Exact_Type()
    {
        var result = Rewrite("""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                [TestMethod]
                [ExpectedException(typeof(System.Exception))]
                public void A() { Work(); }

                private void Work() { }
            }
            """);

        Assert.Contains("Assert.Throws<System.Exception>(", result);
        Assert.DoesNotContain("ThrowsAny", result);
    }
}

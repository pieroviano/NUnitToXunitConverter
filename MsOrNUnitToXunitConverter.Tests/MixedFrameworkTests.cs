using ConversionClassLibrary;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>Covers §1.4 of CONVERSION-GAPS.md and the cross-file half of the assembly fixture.</summary>
public class MixedFrameworkTests
{
    [Theory]
    [InlineData("using NUnit.Framework; [Test] void A() { }")]
    [InlineData("using Microsoft.VisualStudio.TestTools.UnitTesting; [TestMethod] void A() { }")]
    public void Either_Framework_Is_Detected(string content)
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("x.cs").Returns(content);

        var sut = new AnyFrameworkTestDetector { File = file };

        // One detector or the other converted whichever framework was found first and left the other's
        // files untouched, which is exactly what a part-migrated project looks like.
        Assert.True(sut.IsUnitTest("x.cs"));
    }

    [Fact]
    public void A_File_With_Neither_Framework_Is_Not_A_Test()
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("x.cs").Returns("public class Plain { }");

        Assert.False(new AnyFrameworkTestDetector { File = file }.IsUnitTest("x.cs"));
    }

    [Fact]
    public void Setting_File_Reaches_Both_Detectors()
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("x.cs").Returns("[TestClass] class A { }");

        var sut = new AnyFrameworkTestDetector();
        sut.File = file;

        Assert.True(sut.IsUnitTest("x.cs"));
    }

    [Fact]
    public void A_Test_Class_Joins_The_Collection_When_Another_File_Declares_Assembly_Setup()
    {
        var code = """
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [Test]
                public void A() { }
            }
            """;

        // The class declaring [SetUpFixture] is in some other file, so only the shared context knows.
        var rewriter = new XunitSyntaxRewriter
        {
            OneTimeSetUpContext = new OneTimeSetUpContext { ProjectHasAssemblyFixture = true }
        };

        var result = rewriter.Visit(CSharpSyntaxTree.ParseText(code).GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();

        Assert.Contains("[Collection(\"Assembly\")]", result);
    }

    [Fact]
    public void No_Collection_Is_Added_When_The_Project_Has_No_Assembly_Setup()
    {
        var code = """
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [Test]
                public void A() { }
            }
            """;

        var result = new XunitSyntaxRewriter().Visit(CSharpSyntaxTree.ParseText(code).GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();

        Assert.DoesNotContain("Collection", result);
    }

    [Fact]
    public void The_Assembly_Collection_Is_Defined_Only_Once_Across_Files()
    {
        var declaring = """
            using NUnit.Framework;

            [SetUpFixture]
            public class Hooks
            {
                [OneTimeSetUp]
                public void Once() { }
            }
            """;

        var context = new OneTimeSetUpContext { ProjectHasAssemblyFixture = true };

        var first = Convert(declaring, context);
        var second = Convert(declaring, context);

        // Two files declaring assembly-wide setup would otherwise emit two types of the same name.
        Assert.Contains("CollectionDefinition", first);
        Assert.DoesNotContain("CollectionDefinition", second);
        Assert.DoesNotContain("class AssemblyFixture", second);
    }

    private static string Convert(string code, OneTimeSetUpContext context)
    {
        var rewriter = new XunitSyntaxRewriter { OneTimeSetUpContext = context };

        return rewriter.Visit(CSharpSyntaxTree.ParseText(code).GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();
    }
}

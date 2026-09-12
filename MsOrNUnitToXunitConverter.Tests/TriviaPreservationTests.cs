using NSubstitute;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>
/// Covers the preprocessor and formatting items of CONVERSION-GAPS.md §4. These go through
/// <see cref="NUnitToXunitRewriter"/> rather than the rewriter directly, because it is the formatting step
/// there - not the rewrite - that used to discard them.
/// </summary>
public class TriviaPreservationTests
{
    private static string Convert(string source)
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText("Tests.cs").Returns(source);

        string? written = null;
        file.When(f => f.WriteAllText("Tests.cs", Arg.Any<string>()))
            .Do(call => written = call.ArgAt<string>(1));

        new NUnitToXunitRewriter { File = file }.RewriteFile("Tests.cs");

        return written!;
    }

    [Fact]
    public void Preprocessor_Directives_Survive_On_Both_Sides()
    {
        var result = Convert("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                [Test]
                public void A()
                {
            #pragma warning disable CS0219
                    var unused = 1;
            #pragma warning restore CS0219

                    Assert.AreEqual(1, 1);
                }
            }
            """);

        // NormalizeWhitespace dropped the disable and kept the restore; building replacement nodes without
        // the original's trivia then dropped the restore instead.
        Assert.Contains("#pragma warning disable CS0219", result);
        Assert.Contains("#pragma warning restore CS0219", result);
        Assert.Contains("Assert.Equal(1, 1)", result);
    }

    [Fact]
    public void Comments_Survive()
    {
        var result = Convert("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                /// <summary>Why this test exists.</summary>
                [Test]
                public void A()
                {
                    // Explains the assert.
                    Assert.IsTrue(true);
                }
            }
            """);

        Assert.Contains("Why this test exists.", result);
        Assert.Contains("// Explains the assert.", result);
        Assert.Contains("Assert.True(true)", result);
    }

    [Fact]
    public void Untouched_Code_Keeps_Its_Own_Layout()
    {
        var result = Convert("""
            using NUnit.Framework;

            [TestFixture]
            public class Tests
            {
                private static readonly int[] Numbers =
                {
                    1, 2, 3
                };

                [Test]
                public void A() { Assert.AreEqual(1, 1); }
            }
            """);

        // Reformatting the whole file made every conversion a whole-file diff however little changed.
        Assert.Contains("1, 2, 3", result);
    }
}

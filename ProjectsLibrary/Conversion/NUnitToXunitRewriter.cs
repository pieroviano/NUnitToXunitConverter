using ConversionClassLibrary;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Rewrites a single test source file to xUnit. <see cref="XunitSyntaxRewriter"/> recognises the MSTest and
/// NUnit attributes and asserts in the same pass, so the source framework does not have to be known up front.
/// </summary>
public class NUnitToXunitRewriter
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    /// <summary>
    /// Shared by every file of one project. Assembly-wide setup is declared in one file but has to be joined
    /// by the test classes in all the others, so that fact cannot live inside a single file's rewrite.
    /// </summary>
    public OneTimeSetUpContext Context { get; set; } = new OneTimeSetUpContext();

    public void RewriteFile(string path)
    {
        var code = File.ReadAllText(path);

        var tree = CSharpSyntaxTree.ParseText(code);

        var rewriter = new XunitSyntaxRewriter { OneTimeSetUpContext = Context };
        var newRoot = rewriter.Visit(tree.GetRoot());

        File.WriteAllText(path, Format(newRoot!));
    }

    /// <summary>
    /// Formats through Roslyn's formatter rather than <c>NormalizeWhitespace()</c>.
    /// </summary>
    /// <remarks>
    /// NormalizeWhitespace rewrites the whole file, which discarded preprocessor directives - a
    /// <c>#pragma warning disable</c> vanished while its matching <c>restore</c> survived - and turned every
    /// conversion into a whole-file diff however little had actually changed. The formatter keeps trivia and
    /// only lays out what the rewrite touched.
    /// </remarks>
    private static string Format(SyntaxNode root)
    {
        using var workspace = new AdhocWorkspace();

        return Formatter.Format(root, workspace).ToFullString();
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Rewrites a single test source file to xUnit. <see cref="XunitSyntaxRewriter"/> recognises the MSTest and
/// NUnit attributes and asserts in the same pass, so the source framework does not have to be known up front.
/// </summary>
public class NUnitToXunitRewriter
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    public void RewriteFile(string path)
    {
        var code = File.ReadAllText(path);

        var tree = CSharpSyntaxTree.ParseText(code);

        var rewriter = new XunitSyntaxRewriter();
        var newRoot = rewriter.Visit(tree.GetRoot());

        var contents = newRoot!.NormalizeWhitespace().ToFullString();
        File.WriteAllText(path, contents);
    }
}

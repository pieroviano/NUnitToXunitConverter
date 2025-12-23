using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NUnitToXunitConverter.Conversion;

static class NUnitToXunitRewriter
{
    public static void RewriteFile(string path)
    {
        var code = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(code);

        var rewriter = new XunitSyntaxRewriter();
        var newRoot = rewriter.Visit(tree.GetRoot());

        var contents = newRoot!.NormalizeWhitespace().ToFullString();
        File.WriteAllText(path, contents);
    }
}
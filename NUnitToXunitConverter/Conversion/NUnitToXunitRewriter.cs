using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NUnitToXunitConverter.Conversion;

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
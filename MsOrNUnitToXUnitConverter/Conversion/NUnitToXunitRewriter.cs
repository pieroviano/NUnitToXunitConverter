using ConversionClassLibrary;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MsOrNUnitToXunitConverter.Conversion;

public class NUnitToXunitRewriter(bool hasMsTests)
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    public void RewriteFile(string path)
    {
        var code = File.ReadAllText(path);
        if (hasMsTests)
        {
            code = new MsTestToNUnitContent().TransformMethods(code);
        }
        var tree = CSharpSyntaxTree.ParseText(code);

        var rewriter = new XunitSyntaxRewriter();
        var newRoot = rewriter.Visit(tree.GetRoot());

        var contents = newRoot!.NormalizeWhitespace().ToFullString();
        File.WriteAllText(path, contents);
    }
}
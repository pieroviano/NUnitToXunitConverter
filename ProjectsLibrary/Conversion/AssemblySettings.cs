using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Translates the assembly-level settings both frameworks use to control parallelism. xUnit expresses the
/// same thing through <c>[assembly: CollectionBehavior]</c>, and where it has no equivalent the setting is
/// dropped - it holds no assertions, so losing it changes nothing about what the tests check.
/// </summary>
public static class AssemblySettings
{
    /// <summary>Attribute names that are consumed here and must not survive the rewrite.</summary>
    public static readonly string[] Names =
    [
        // MSTest
        "Parallelize", "DoNotParallelize", "ClassCleanupExecution",
        // NUnit
        "Parallelizable", "LevelOfParallelism", "FixtureLifeCycle", "NonTestAssembly"
    ];

    /// <summary>
    /// The <c>[assembly: CollectionBehavior(...)]</c> the file's settings amount to, or
    /// <see langword="null"/> when none of them has an xUnit counterpart.
    /// </summary>
    public static AttributeListSyntax? CollectionBehavior(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var arguments = new List<AttributeArgumentSyntax>();

        foreach (var attribute in attributeLists
                     .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                     .SelectMany(list => list.Attributes))
        {
            var name = attribute.Name.ToString();
            var first = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;

            switch (name)
            {
                // MSTest's opt-out, and NUnit's ParallelScope.None, both mean "run nothing in parallel".
                case "DoNotParallelize":
                case "Parallelizable" when first?.ToString().EndsWith("None", StringComparison.Ordinal) == true:
                    arguments.Add(Named("DisableTestParallelization", LiteralExpression(SyntaxKind.TrueLiteralExpression)));

                    break;

                case "LevelOfParallelism" when first != null:
                    arguments.Add(Named("MaxParallelThreads", first));

                    break;
            }
        }

        if (arguments.Count == 0)
            return null;

        return AttributeList(
                SingletonSeparatedList(
                    Attribute(IdentifierName("CollectionBehavior"))
                        .WithArgumentList(AttributeArgumentList(SeparatedList(arguments)))))
            .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)));
    }

    private static AttributeArgumentSyntax Named(string name, ExpressionSyntax value)
    {
        return AttributeArgument(value).WithNameEquals(NameEquals(IdentifierName(name)));
    }
}

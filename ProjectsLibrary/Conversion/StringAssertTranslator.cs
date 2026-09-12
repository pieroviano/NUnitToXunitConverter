using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Translates <c>StringAssert</c>, which both frameworks have with the same member names and the arguments
/// the other way round: NUnit takes <c>(expected, actual)</c>, MSTest takes <c>(value, substring)</c>. xUnit
/// follows NUnit's order — the needle first — so the MSTest calls have to be swapped and the NUnit ones must
/// not be. When the source framework cannot be established the call is left alone rather than guessed at.
/// </summary>
public static class StringAssertTranslator
{
    /// <summary>Member name to the xUnit assert it becomes.</summary>
    private static readonly Dictionary<string, string> Targets = new()
    {
        ["Contains"] = "Contains",
        ["DoesNotContain"] = "DoesNotContain",
        ["StartsWith"] = "StartsWith",
        ["EndsWith"] = "EndsWith",
        // NUnit spells the regex asserts IsMatch/DoesNotMatch, MSTest spells them Matches/DoesNotMatch.
        ["IsMatch"] = "Matches",
        ["Matches"] = "Matches",
        ["DoesNotMatch"] = "DoesNotMatch"
    };

    /// <summary>NUnit only: a case-insensitive comparison, which xUnit expresses as an argument.</summary>
    private const string IgnoringCase = "AreEqualIgnoringCase";

    public static InvocationExpressionSyntax? Rewrite(InvocationExpressionSyntax node, Framework framework)
    {
        if (node.Expression is not MemberAccessExpressionSyntax member ||
            member.Expression.ToString() != "StringAssert")
        {
            return null;
        }

        // Without knowing the framework the argument order is a coin toss, and getting it wrong reverses the
        // meaning of the assert silently.
        if (framework == Framework.Unknown)
            return null;

        var name = member.Name.Identifier.Text;
        var args = node.ArgumentList.Arguments;

        if (args.Count < 2)
            return null;

        if (name == IgnoringCase && framework == Framework.NUnit)
        {
            return Call(
                node,
                IdentifierName("Equal"),
                args[0],
                args[1],
                Argument(LiteralExpression(SyntaxKind.TrueLiteralExpression))
                    .WithNameColon(NameColon(IdentifierName("ignoreCase"))));
        }

        if (!Targets.TryGetValue(name, out var target))
            return null;

        // "Matches" is MSTest's name and "IsMatch" NUnit's, so a file using the other framework's spelling is
        // not really this assert at all.
        if ((name == "Matches" && framework != Framework.MsTest) ||
            (name == "IsMatch" && framework != Framework.NUnit))
        {
            return null;
        }

        var needle = framework == Framework.NUnit ? args[0] : args[1];
        var haystack = framework == Framework.NUnit ? args[1] : args[0];

        return Call(node, IdentifierName(target), needle, haystack);
    }

    private static InvocationExpressionSyntax Call(
        InvocationExpressionSyntax node,
        SimpleNameSyntax name,
        params ArgumentSyntax[] arguments)
    {
        return node
            .WithExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Assert"),
                    name))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));
    }
}

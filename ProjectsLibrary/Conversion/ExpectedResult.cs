using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Converts NUnit's <c>[TestCase(…, ExpectedResult = x)]</c>, where the test returns the value under test and
/// the framework compares it. xUnit theories return void and <c>[InlineData]</c> has no such property, so the
/// expectation becomes a trailing parameter and the comparison becomes an explicit assert.
/// </summary>
public static class ExpectedResult
{
    private const string ParameterName = "expectedResult";

    public static MethodDeclarationSyntax Rewrite(MethodDeclarationSyntax method)
    {
        var rows = method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute => attribute.Name.ToString() == "InlineData")
            .Where(HasExpectedResult)
            .ToList();

        if (rows.Count == 0)
            return method;

        // Each row's expectation moves from the named property to the end of the positional arguments, which
        // is where the new parameter sits.
        method = method.ReplaceNodes(rows, (original, _) => Flatten(original));

        if (method.ReturnType is PredefinedTypeSyntax predefined &&
            predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return method;
        }

        var returnType = method.ReturnType;

        method = method
            .WithParameterList(
                method.ParameterList.AddParameters(
                    Parameter(Identifier(ParameterName)).WithType(returnType)))
            .WithReturnType(PredefinedType(Token(SyntaxKind.VoidKeyword)));

        return method.ExpressionBody != null
            ? FromExpressionBody(method)
            : FromBlockBody(method);
    }

    private static bool HasExpectedResult(AttributeSyntax attribute)
    {
        return ExpectedResultArgument(attribute) != null;
    }

    private static AttributeArgumentSyntax? ExpectedResultArgument(AttributeSyntax attribute)
    {
        return attribute.ArgumentList?.Arguments
            .FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.Text == "ExpectedResult");
    }

    private static AttributeSyntax Flatten(AttributeSyntax attribute)
    {
        var expected = ExpectedResultArgument(attribute);

        if (expected == null)
            return attribute;

        var positional = attribute.ArgumentList!.Arguments
            .Where(argument => argument != expected)
            .Append(AttributeArgument(expected.Expression));

        return attribute.WithArgumentList(AttributeArgumentList(SeparatedList(positional)));
    }

    /// <summary><c>=> a + 1</c> becomes <c>{ Assert.Equal(expectedResult, a + 1); }</c>.</summary>
    private static MethodDeclarationSyntax FromExpressionBody(MethodDeclarationSyntax method)
    {
        return method
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(Block(Compare(method.ExpressionBody!.Expression)));
    }

    /// <summary>Every <c>return x;</c> becomes an assert followed by a bare return.</summary>
    private static MethodDeclarationSyntax FromBlockBody(MethodDeclarationSyntax method)
    {
        if (method.Body == null)
            return method;

        var returns = method.Body
            .DescendantNodes()
            // A return inside a nested lambda or local function belongs to that, not to the test.
            .Where(descendant => descendant is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<ReturnStatementSyntax>()
            .Where(statement => statement.Expression != null)
            .ToList();

        if (returns.Count == 0)
            return method;

        return method.WithBody(
            method.Body.ReplaceNodes(
                returns,
                (original, _) => Block(Compare(original.Expression!), ReturnStatement())));
    }

    private static StatementSyntax Compare(ExpressionSyntax actual)
    {
        return ExpressionStatement(
            InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Assert"),
                        IdentifierName("Equal")))
                .WithArgumentList(
                    ArgumentList(
                        SeparatedList(new[]
                        {
                            Argument(IdentifierName(ParameterName)),
                            Argument(actual)
                        }))));
    }
}

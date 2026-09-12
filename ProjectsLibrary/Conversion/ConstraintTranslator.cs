using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Translates NUnit's constraint model, <c>Assert.That(actual, Is.EqualTo(expected))</c>, which has been the
/// idiomatic form since NUnit 3 and is the only one left in NUnit 4.
/// </summary>
/// <remarks>
/// A constraint is an open-ended expression tree, so this recognises a fixed vocabulary and returns
/// <see langword="null"/> for everything else, leaving the call for a human rather than approximating it.
/// Note the argument flip: NUnit reads actual-then-expected, every xUnit assert reads expected-then-actual.
/// </remarks>
public static class ConstraintTranslator
{
    public static InvocationExpressionSyntax? Rewrite(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax member ||
            member.Expression.ToString() != "Assert" ||
            member.Name.Identifier.Text != "That")
        {
            return null;
        }

        var arguments = node.ArgumentList.Arguments;

        if (arguments.Count < 2)
            return null;

        var actual = arguments[0];

        if (!TryRead(arguments[1].Expression, out var path, out var constraintArguments, out var typeArgument))
            return null;

        return Translate(node, path, actual, constraintArguments, typeArgument);
    }

    private static InvocationExpressionSyntax? Translate(
        InvocationExpressionSyntax node,
        string path,
        ArgumentSyntax actual,
        IReadOnlyList<ArgumentSyntax> constraint,
        TypeSyntax? typeArgument)
    {
        ArgumentSyntax? First() => constraint.Count > 0 ? constraint[0] : null;

        switch (path)
        {
            // ---- equality and identity ----
            case "Is.EqualTo" when First() != null:
                return Call(node, "Equal", First()!, actual);
            case "Is.Not.EqualTo" when First() != null:
                return Call(node, "NotEqual", First()!, actual);
            case "Is.SameAs" when First() != null:
                return Call(node, "Same", First()!, actual);
            case "Is.Not.SameAs" when First() != null:
                return Call(node, "NotSame", First()!, actual);

            // ---- unary states ----
            case "Is.Null":
                return Call(node, "Null", actual);
            case "Is.Not.Null":
                return Call(node, "NotNull", actual);
            case "Is.True":
                return Call(node, "True", actual);
            case "Is.False":
                return Call(node, "False", actual);
            case "Is.Not.True":
                return Call(node, "False", actual);
            case "Is.Not.False":
                return Call(node, "True", actual);
            case "Is.Empty":
                return Call(node, "Empty", actual);
            case "Is.Not.Empty":
                return Call(node, "NotEmpty", actual);

            // ---- ordering: xUnit has no comparison assert, so the comparison is made explicit ----
            case "Is.GreaterThan" when First() != null:
                return Comparison(node, actual, SyntaxKind.GreaterThanExpression, First()!);
            case "Is.GreaterThanOrEqualTo" when First() != null:
                return Comparison(node, actual, SyntaxKind.GreaterThanOrEqualExpression, First()!);
            case "Is.LessThan" when First() != null:
                return Comparison(node, actual, SyntaxKind.LessThanExpression, First()!);
            case "Is.LessThanOrEqualTo" when First() != null:
                return Comparison(node, actual, SyntaxKind.LessThanOrEqualExpression, First()!);

            case "Is.InRange" when constraint.Count == 2:
                return Call(node, "InRange", actual, constraint[0], constraint[1]);

            // ---- types ----
            case "Is.TypeOf" when typeArgument != null:
                return Generic(node, "IsType", typeArgument, actual);
            case "Is.InstanceOf" when typeArgument != null:
                return Generic(node, "IsAssignableFrom", typeArgument, actual);
            case "Is.Not.InstanceOf" when typeArgument != null:
                return Generic(node, "IsNotType", typeArgument, actual);

            // ---- containment and text ----
            case "Does.Contain" when First() != null:
            case "Contains.Item" when First() != null:
                return Call(node, "Contains", First()!, actual);
            case "Does.Not.Contain" when First() != null:
                return Call(node, "DoesNotContain", First()!, actual);
            case "Does.StartWith" when First() != null:
                return Call(node, "StartsWith", First()!, actual);
            case "Does.EndWith" when First() != null:
                return Call(node, "EndsWith", First()!, actual);
            case "Does.Match" when First() != null:
                return Call(node, "Matches", First()!, actual);
            case "Does.Not.Match" when First() != null:
                return Call(node, "DoesNotMatch", First()!, actual);

            // ---- exceptions: here "actual" is the delegate under test ----
            case "Throws.TypeOf" when typeArgument != null:
                return Generic(node, "Throws", typeArgument, actual);
            case "Throws.InstanceOf" when typeArgument != null:
                return Generic(node, "ThrowsAny", typeArgument, actual);

            default:
                return null;
        }
    }

    /// <summary>
    /// Flattens a constraint into a dotted path, its arguments and its type argument:
    /// <c>Is.Not.EqualTo(1)</c> reads as <c>("Is.Not.EqualTo", [1], null)</c> and
    /// <c>Is.InstanceOf&lt;string&gt;()</c> as <c>("Is.InstanceOf", [], string)</c>.
    /// </summary>
    private static bool TryRead(
        ExpressionSyntax constraint,
        out string path,
        out IReadOnlyList<ArgumentSyntax> arguments,
        out TypeSyntax? typeArgument)
    {
        path = string.Empty;
        arguments = [];
        typeArgument = null;

        var expression = constraint;

        if (expression is InvocationExpressionSyntax invocation)
        {
            arguments = invocation.ArgumentList.Arguments.ToList();
            expression = invocation.Expression;
        }

        var parts = new List<string>();

        while (true)
        {
            switch (expression)
            {
                case MemberAccessExpressionSyntax member:
                    if (!TryName(member.Name, parts, ref typeArgument))
                        return false;

                    expression = member.Expression;

                    continue;

                case IdentifierNameSyntax identifier:
                    parts.Add(identifier.Identifier.Text);
                    parts.Reverse();
                    path = string.Join(".", parts);

                    return true;

                default:
                    return false;
            }
        }
    }

    private static bool TryName(SimpleNameSyntax name, List<string> parts, ref TypeSyntax? typeArgument)
    {
        switch (name)
        {
            case GenericNameSyntax generic when generic.TypeArgumentList.Arguments.Count == 1:
                typeArgument = generic.TypeArgumentList.Arguments[0];
                parts.Add(generic.Identifier.Text);

                return true;

            case GenericNameSyntax:
                return false;

            default:
                parts.Add(name.Identifier.Text);

                return true;
        }
    }

    private static InvocationExpressionSyntax Comparison(
        InvocationExpressionSyntax node,
        ArgumentSyntax actual,
        SyntaxKind comparison,
        ArgumentSyntax bound)
    {
        return Call(
            node,
            "True",
            Argument(BinaryExpression(comparison, actual.Expression, bound.Expression)));
    }

    private static InvocationExpressionSyntax Call(
        InvocationExpressionSyntax node,
        string name,
        params ArgumentSyntax[] arguments)
    {
        return Rebuild(node, IdentifierName(name), arguments);
    }

    private static InvocationExpressionSyntax Generic(
        InvocationExpressionSyntax node,
        string name,
        TypeSyntax typeArgument,
        params ArgumentSyntax[] arguments)
    {
        return Rebuild(
            node,
            GenericName(Identifier(name))
                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(typeArgument))),
            arguments);
    }

    private static InvocationExpressionSyntax Rebuild(
        InvocationExpressionSyntax node,
        SimpleNameSyntax name,
        IEnumerable<ArgumentSyntax> arguments)
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

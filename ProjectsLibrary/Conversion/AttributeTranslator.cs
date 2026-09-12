using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Translates the attributes that do not survive as attributes. xUnit expresses most of what MSTest and NUnit
/// put in separate attributes as either a named argument on <c>[Fact]</c>/<c>[Theory]</c> (<c>Skip</c>,
/// <c>Timeout</c>) or a <c>[Trait]</c>, so these have to be read off the original declaration and folded in
/// rather than renamed in place.
/// </summary>
public static class AttributeTranslator
{
    /// <summary>Attributes whose content is re-expressed elsewhere, so the original must be dropped.</summary>
    public static readonly string[] Consumed =
    [
        "Ignore", "Explicit", "Timeout",
        "Category", "TestCategory", "Description", "Author", "Owner", "Priority",
        "Property", "TestProperty",
        "TestCaseSource", "DynamicData",
        "DataTestMethod"
    ];

    /// <summary>Attribute name to the trait key it becomes. A trailing <c>null</c> key means "use argument 0".</summary>
    private static readonly Dictionary<string, string?> TraitKeys = new()
    {
        ["Category"] = "Category",
        ["TestCategory"] = "Category",
        ["Description"] = "Description",
        ["Author"] = "Author",
        ["Owner"] = "Owner",
        ["Priority"] = "Priority",
        // NUnit's [Property("k", v)] and MSTest's [TestProperty("k", v)] carry their own key.
        ["Property"] = null,
        ["TestProperty"] = null
    };

    /// <summary>The <c>[Trait]</c> attributes that the declaration's annotations become.</summary>
    public static List<AttributeListSyntax> Traits(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var traits = new List<AttributeListSyntax>();

        foreach (var attribute in All(attributeLists))
        {
            if (!TraitKeys.TryGetValue(attribute.Name.ToString(), out var key))
                continue;

            var arguments = attribute.ArgumentList?.Arguments ?? default;

            if (arguments.Count == 0)
                continue;

            var keyExpression = key != null
                ? FixtureBuilder.StringLiteral(key)
                : AsString(arguments[0].Expression);

            var valueExpression = key != null
                ? AsString(arguments[0].Expression)
                : arguments.Count > 1
                    ? AsString(arguments[1].Expression)
                    : FixtureBuilder.StringLiteral(string.Empty);

            traits.Add(
                AttributeList(
                    SingletonSeparatedList(
                        Attribute(IdentifierName("Trait"))
                            .WithArgumentList(
                                AttributeArgumentList(
                                    SeparatedList(new[]
                                    {
                                        AttributeArgument(keyExpression),
                                        AttributeArgument(valueExpression)
                                    }))))));
        }

        return traits;
    }

    /// <summary>
    /// The <c>Skip</c> reason, if the declaration is ignored or explicit. xUnit has no runtime "explicit", so
    /// an explicit test becomes a skipped one - it is the reading that keeps the suite green either way.
    /// </summary>
    public static ExpressionSyntax? SkipReason(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in All(attributeLists))
        {
            var name = attribute.Name.ToString();

            if (name is not ("Ignore" or "Explicit"))
                continue;

            var reason = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;

            return reason ?? FixtureBuilder.StringLiteral(name == "Ignore" ? "Ignored" : "Explicit");
        }

        return null;
    }

    /// <summary>The milliseconds of a <c>[Timeout(...)]</c>, which becomes <c>[Fact(Timeout = …)]</c>.</summary>
    public static ExpressionSyntax? Timeout(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return All(attributeLists)
            .Where(attribute => attribute.Name.ToString() == "Timeout")
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression)
            .FirstOrDefault(argument => argument != null);
    }

    /// <summary>
    /// <c>[TestCaseSource]</c> and <c>[DynamicData]</c> become <c>[MemberData]</c>. Only the attribute is
    /// translated: NUnit sources yield <c>TestCaseData</c> and MSTest sources <c>object[]</c>, where xUnit
    /// wants <c>IEnumerable&lt;object[]&gt;</c>, so the source member itself may still need adjusting.
    /// </summary>
    public static List<AttributeListSyntax> MemberData(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var translated = new List<AttributeListSyntax>();

        foreach (var attribute in All(attributeLists))
        {
            var name = attribute.Name.ToString();

            if (name is not ("TestCaseSource" or "DynamicData"))
                continue;

            var arguments = attribute.ArgumentList?.Arguments ?? default;

            if (arguments.Count == 0)
                continue;

            // NUnit puts the declaring type first, MSTest puts it second; MSTest may instead pass a
            // DynamicDataSourceType, which xUnit infers and so has nothing to carry over.
            var memberName = arguments
                .Select(argument => argument.Expression)
                .FirstOrDefault(expression => expression is not TypeOfExpressionSyntax);

            var declaringType = arguments
                .Select(argument => argument.Expression)
                .OfType<TypeOfExpressionSyntax>()
                .FirstOrDefault();

            if (memberName == null)
                continue;

            var memberArguments = new List<AttributeArgumentSyntax> { AttributeArgument(memberName) };

            if (declaringType != null)
            {
                memberArguments.Add(
                    AttributeArgument(declaringType)
                        .WithNameEquals(NameEquals(IdentifierName("MemberType"))));
            }

            translated.Add(
                AttributeList(
                    SingletonSeparatedList(
                        Attribute(IdentifierName("MemberData"))
                            .WithArgumentList(AttributeArgumentList(SeparatedList(memberArguments))))));
        }

        return translated;
    }

    /// <summary>
    /// Adds <c>Skip</c>/<c>Timeout</c> to the <c>[Fact]</c> or <c>[Theory]</c> already on the declaration.
    /// </summary>
    public static SyntaxList<AttributeListSyntax> WithFactArguments(
        SyntaxList<AttributeListSyntax> attributeLists,
        ExpressionSyntax? skip,
        ExpressionSyntax? timeout)
    {
        if (skip == null && timeout == null)
            return attributeLists;

        var named = new List<AttributeArgumentSyntax>();

        if (skip != null)
            named.Add(AttributeArgument(skip).WithNameEquals(NameEquals(IdentifierName("Skip"))));

        if (timeout != null)
            named.Add(AttributeArgument(timeout).WithNameEquals(NameEquals(IdentifierName("Timeout"))));

        return SyntaxFactory.List(attributeLists.Select(list =>
            list.WithAttributes(
                SeparatedList(list.Attributes.Select(attribute =>
                    attribute.Name.ToString() is "Fact" or "Theory"
                        ? attribute.WithArgumentList(
                            AttributeArgumentList(
                                SeparatedList((attribute.ArgumentList?.Arguments ?? default).Concat(named))))
                        : attribute)))));
    }

    private static IEnumerable<AttributeSyntax> All(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists.SelectMany(list => list.Attributes);
    }

    /// <summary>
    /// Traits are compile-time constant strings. A string literal passes straight through; any other literal
    /// - <c>[Priority(1)]</c> is the common one - becomes its own text; anything else is left alone rather
    /// than guessed at, and will be flagged by the compiler if it is not already a string.
    /// </summary>
    private static ExpressionSyntax AsString(ExpressionSyntax expression)
    {
        if (expression is not LiteralExpressionSyntax literal)
            return expression;

        return literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal
            : FixtureBuilder.StringLiteral(literal.Token.ValueText);
    }
}

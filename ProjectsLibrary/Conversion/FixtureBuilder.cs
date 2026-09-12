using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// Builds the types xUnit uses where the other frameworks use attributes: a fixture class holding what ran
/// once per class or once per assembly, and the collection definition that gives an assembly-wide fixture its
/// scope.
/// </summary>
public static class FixtureBuilder
{
    /// <summary>The generated assembly-wide fixture, and the collection that carries it.</summary>
    public const string AssemblyFixtureName = "AssemblyFixture";

    public const string AssemblyCollectionName = "AssemblyCollection";

    /// <summary>The string both <c>[CollectionDefinition]</c> and <c>[Collection]</c> are keyed on.</summary>
    public const string AssemblyCollectionKey = "Assembly";

    /// <summary>
    /// A fixture class: setup statements become the constructor, teardown statements become
    /// <c>Dispose</c> and bring <c>IDisposable</c> with them.
    /// </summary>
    public static ClassDeclarationSyntax Fixture(
        string name,
        IReadOnlyList<StatementSyntax> setUp,
        IReadOnlyList<StatementSyntax> tearDown)
    {
        var members = new List<MemberDeclarationSyntax>
        {
            ConstructorDeclaration(name)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithBody(Block(setUp))
        };

        var fixture = ClassDeclaration(name)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));

        if (tearDown.Count > 0)
        {
            members.Add(
                MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), "Dispose")
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithBody(Block(tearDown)));

            fixture = fixture.WithBaseList(
                BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                    SimpleBaseType(ParseTypeName("System.IDisposable")))));
        }

        return fixture.WithMembers(List(members));
    }

    /// <summary>
    /// <c>[CollectionDefinition("Assembly")] public class AssemblyCollection : ICollectionFixture&lt;…&gt; { }</c>
    /// — the only way xUnit shares one fixture instance across every test class in a run, which is what
    /// <c>[AssemblyInitialize]</c> and NUnit's <c>[SetUpFixture]</c> mean.
    /// </summary>
    public static ClassDeclarationSyntax AssemblyCollectionDefinition()
    {
        return ClassDeclaration(AssemblyCollectionName)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithAttributeLists(
                SingletonList(
                    AttributeList(
                        SingletonSeparatedList(
                            Attribute(IdentifierName("CollectionDefinition"))
                                .WithArgumentList(
                                    AttributeArgumentList(
                                        SingletonSeparatedList(
                                            AttributeArgument(StringLiteral(AssemblyCollectionKey)))))))))
            .WithBaseList(
                BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                    SimpleBaseType(ParseTypeName($"ICollectionFixture<{AssemblyFixtureName}>")))));
    }

    /// <summary><c>[Collection("Assembly")]</c>, which opts a test class into the shared fixture.</summary>
    public static AttributeListSyntax AssemblyCollectionAttribute()
    {
        return AttributeList(
            SingletonSeparatedList(
                Attribute(IdentifierName("Collection"))
                    .WithArgumentList(
                        AttributeArgumentList(
                            SingletonSeparatedList(
                                AttributeArgument(StringLiteral(AssemblyCollectionKey)))))));
    }

    public static LiteralExpressionSyntax StringLiteral(string value)
    {
        return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
    }
}

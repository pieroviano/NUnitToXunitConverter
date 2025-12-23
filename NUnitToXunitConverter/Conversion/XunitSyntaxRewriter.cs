using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace NUnitToXunitConverter.Conversion;

class XunitSyntaxRewriter : CSharpSyntaxRewriter
{
    private bool _hasSetUp;
    private bool _hasTearDown;
    private bool _hasOneTimeSetUp;
    private bool _needsOutputHelper;

    private MethodDeclarationSyntax? _setUpMethod;
    private MethodDeclarationSyntax? _tearDownMethod;
    private MethodDeclarationSyntax? _oneTimeSetUpMethod;

    private string? _testClassName;

    // ---------------- USING ----------------

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name?.ToString() == "NUnit.Framework")
            return UsingDirective(ParseName("Xunit"));

        return base.VisitUsingDirective(node);
    }

    // ---------------- ATTRIBUTES ----------------

    public override SyntaxNode? VisitAttribute(AttributeSyntax node)
    {
        var name = node.Name.ToString();

        return name switch
        {
            "Test" => node.WithName(IdentifierName("Fact")),
            "TestCase" => node.WithName(IdentifierName("InlineData")),
            "TestFixture" => null,
            "SetUpFixture" => null,
            "OneTimeSetUp" => null,
            _ => base.VisitAttribute(node)
        };
    }

    public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
    {
        var attrs = node.Attributes
            .Select(a => (AttributeSyntax?)VisitAttribute(a))
            .Where(a => a != null)
            .ToList();

        if (attrs.Count == 0)
            return null;

#pragma warning disable CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
        return node.WithAttributes(SeparatedList(attrs)!);
#pragma warning restore CS8631 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match constraint type.
    }

    // ---------------- CLASS ----------------

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        _testClassName = node.Identifier.Text;

        foreach (var method in node.Members.OfType<MethodDeclarationSyntax>())
        {
            foreach (var attr in method.AttributeLists.SelectMany(a => a.Attributes))
            {
                switch (attr.Name.ToString())
                {
                    case "SetUp":
                        _hasSetUp = true;
                        _setUpMethod = method;
                        break;
                    case "TearDown":
                        _hasTearDown = true;
                        _tearDownMethod = method;
                        break;
                    case "OneTimeSetUp":
                        _hasOneTimeSetUp = true;
                        _oneTimeSetUpMethod = method;
                        break;
                }
            }
        }

        var newNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

        var members = newNode.Members
            .Where(m => m != _setUpMethod && m != _tearDownMethod && m != _oneTimeSetUpMethod)
            .ToList();

        // ---- ITestOutputHelper field ----
        if (_needsOutputHelper)
        {
            members.Insert(0,
                FieldDeclaration(
                    VariableDeclaration(
                        IdentifierName("ITestOutputHelper"),
                        SingletonSeparatedList(
                            VariableDeclarator("_output"))))
                .WithModifiers(TokenList(
                    Token(SyntaxKind.PrivateKeyword),
                    Token(SyntaxKind.ReadOnlyKeyword))));
        }

        // ---- Constructor (SetUp) ----
        if (_hasSetUp || _needsOutputHelper)
        {
            var ctorBody = new List<StatementSyntax>();

            if (_needsOutputHelper)
            {
                ctorBody.Add(
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            IdentifierName("_output"),
                            IdentifierName("output"))));
            }

            if (_hasSetUp && _setUpMethod != null)
                ctorBody.AddRange(_setUpMethod.Body!.Statements);

            members.Insert(0,
                ConstructorDeclaration(_testClassName!)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(
                    _needsOutputHelper
                        ? ParameterList(
                            SingletonSeparatedList(
                                Parameter(Identifier("output"))
                                .WithType(IdentifierName("ITestOutputHelper"))))
                        : ParameterList())
                .WithBody(Block(ctorBody)));
        }

        // ---- Dispose (TearDown) ----
        if (_hasTearDown && _tearDownMethod != null)
        {
            members.Add(
                MethodDeclaration(
                    PredefinedType(Token(SyntaxKind.VoidKeyword)), "Dispose")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithBody(_tearDownMethod.Body!));

            newNode = AddInterface(newNode, "System.IDisposable");
        }

        // ---- OneTimeSetUp fixture ----
        if (_hasOneTimeSetUp && OneTimeSetUpContext.FixtureClassName == null)
        {
            OneTimeSetUpContext.FixtureClassName = $"{_testClassName}Fixture";
        }

        if (!string.IsNullOrEmpty(OneTimeSetUpContext.FixtureClassName))
        {
            newNode = AddInterface(
                newNode,
                $"IClassFixture<{OneTimeSetUpContext.FixtureClassName}>");
        }

        newNode = newNode.WithMembers(List(members));
        return newNode;
    }

    // ---------------- FIXTURE GENERATION ----------------

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var newNode = (CompilationUnitSyntax)base.VisitCompilationUnit(node)!;

        if (_hasOneTimeSetUp &&
            _oneTimeSetUpMethod != null &&
            OneTimeSetUpContext.FixtureClassName == $"{_testClassName}Fixture")
        {
            var fixture =
                ClassDeclaration(OneTimeSetUpContext.FixtureClassName)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        ConstructorDeclaration(OneTimeSetUpContext.FixtureClassName)
                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                        .WithBody(_oneTimeSetUpMethod.Body!)));

            newNode = newNode.AddMembers(fixture);
        }

        return newNode;
    }

    // ---------------- ASSERT TRANSLATION ----------------

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax inv &&
            inv.Expression is MemberAccessExpressionSyntax ma &&
            ma.Expression.ToString() == "Assert" &&
            inv.ArgumentList.Arguments.Count == 3)
        {
            _needsOutputHelper = true;

            var message = inv.ArgumentList.Arguments[2];

            return TryStatement(
                Block(
                    ExpressionStatement(
                        (InvocationExpressionSyntax)VisitInvocationExpression(inv)!)),
                SingletonList(
                    CatchClause()
                        .WithBlock(
                            Block(
                                ExpressionStatement(
                                    InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                IdentifierName("_output"),
                                                IdentifierName("WriteLine")))
                                        .WithArgumentList(
                                            ArgumentList(
                                                SingletonSeparatedList(message)))),
                                ThrowStatement()))),
                null);
        }

        return base.VisitExpressionStatement(node);
    }
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax ma ||
            ma.Expression.ToString() != "Assert")
            return base.VisitInvocationExpression(node);

        var method = ma.Name.Identifier.Text;
        var args = node.ArgumentList.Arguments;

        string? target = method switch
        {
            "AreEqual" => "Equal",
            "AreNotEqual" => "NotEqual",
            "IsTrue" => "True",
            "IsFalse" => "False",
            "IsNull" => "Null",
            "IsNotNull" => "NotNull",
            _ => null
        };

        if (target == null)
            return base.VisitInvocationExpression(node);

        if (args.Count == 3)
            _needsOutputHelper = true;

        return node.WithExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Assert"),
                    IdentifierName(target)))
            .WithArgumentList(
                ArgumentList(
                    args.Count == 3
                        ? SeparatedList(new[] { args[0], args[1] })
                        : args));
    }

    // ---------------- HELPERS ----------------

    private static ClassDeclarationSyntax AddInterface(
        ClassDeclarationSyntax cls,
        string iface)
    {
        var baseType = SimpleBaseType(ParseTypeName(iface));

        return cls.BaseList == null
            ? cls.WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(baseType)))
            : cls.WithBaseList(cls.BaseList.AddTypes(baseType));
    }
}

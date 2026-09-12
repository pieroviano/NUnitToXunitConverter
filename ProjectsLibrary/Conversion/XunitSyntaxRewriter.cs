using ConversionClassLibrary;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

public class XunitSyntaxRewriter : CSharpSyntaxRewriter
{
    private bool _currentMethodIsAsync;
    private bool _hasSetUp;
    private bool _hasTearDown;
    private bool _hasOneTimeSetUp;
    private bool _needsOutputHelper;

    private MethodDeclarationSyntax? _setUpMethod;
    private MethodDeclarationSyntax? _tearDownMethod;
    private MethodDeclarationSyntax? _oneTimeSetUpMethod;

    private string? _testClassName;
    internal OneTimeSetUpContext OneTimeSetUpContext = new OneTimeSetUpContext();

    // ---------------- USING ----------------

    private static readonly string[] TestFrameworkNamespaces =
    [
        "NUnit.Framework",
        "Microsoft.VisualStudio.TestTools.UnitTesting"
    ];

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (TestFrameworkNamespaces.Contains(node.Name?.ToString()))
            return UsingDirective(ParseName("Xunit"));

        return base.VisitUsingDirective(node);
    }

    // ---------------- ATTRIBUTES ----------------

    public override SyntaxNode? VisitAttribute(AttributeSyntax node)
    {
        var name = node.Name.ToString();

        return name switch
        {
            // NUnit
            "Test" => node.WithName(IdentifierName("Fact")),
            "TestCase" => node.WithName(IdentifierName("InlineData")),
            "TestFixture" => null,
            "SetUpFixture" => null,
            "OneTimeSetUp" => null,
            // MSTest
            "TestMethod" => node.WithName(IdentifierName("Fact")),
            "DataRow" => node.WithName(IdentifierName("InlineData")),
            "TestClass" => null,
            // The exception type is lifted into an Assert.Throws call by VisitMethodDeclaration.
            "ExpectedException" => null,
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
                    case "TestInitialize":
                        _hasSetUp = true;
                        _setUpMethod = method;
                        break;
                    case "TearDown":
                    case "TestCleanup":
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

    // ---------------- METHODS ----------------

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var expectedException = FindExpectedExceptionType(node);
        var isAsync = IsAsync(node);

        var enclosingWasAsync = _currentMethodIsAsync;
        _currentMethodIsAsync = isAsync;
        var rewritten = (MethodDeclarationSyntax?)base.VisitMethodDeclaration(node);
        _currentMethodIsAsync = enclosingWasAsync;

        if (rewritten == null)
            return null;

        // [ExpectedException(typeof(T))] has no xUnit equivalent: the whole body becomes the delegate
        // passed to Assert.Throws<T> / Assert.ThrowsAsync<T>.
        if (expectedException != null && rewritten.Body != null)
        {
            rewritten = rewritten.WithBody(
                Block(ThrowsAssert(expectedException, rewritten.Body, isAsync)));
        }

        // Awaits introduced above (or by VisitExpressionStatement) need the method to be async.
        if (isAsync &&
            !rewritten.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
            rewritten.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
        {
            rewritten = rewritten.WithModifiers(
                rewritten.Modifiers.Add(Token(SyntaxKind.AsyncKeyword)));
        }

        return rewritten;
    }

    private static bool IsAsync(MethodDeclarationSyntax node)
    {
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return true;

        var returnType = node.ReturnType.ToString();

        return returnType is "Task" or "ValueTask"
               || returnType.StartsWith("Task<", StringComparison.Ordinal)
               || returnType.StartsWith("ValueTask<", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads <c>T</c> out of <c>[ExpectedException(typeof(T))]</c>, tolerating the optional message and
    /// AllowDerivedTypes arguments MSTest allows after it.
    /// </summary>
    private static TypeSyntax? FindExpectedExceptionType(MethodDeclarationSyntax node)
    {
        var attribute = node.AttributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "ExpectedException");

        return attribute?.ArgumentList?.Arguments
            .Select(argument => argument.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .FirstOrDefault()
            ?.Type;
    }

    /// <summary>
    /// Builds <c>await Assert.ThrowsAsync&lt;T&gt;(async () => { body })</c> for asynchronous methods and
    /// <c>Assert.Throws&lt;T&gt;(() => { body })</c> for synchronous ones. The body moves across unchanged,
    /// so any await it already contains keeps working inside the async delegate.
    /// </summary>
    private static StatementSyntax ThrowsAssert(TypeSyntax exceptionType, BlockSyntax body, bool isAsync)
    {
        var lambda = ParenthesizedLambdaExpression().WithBlock(body);

        if (isAsync)
            lambda = lambda.WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword));

        ExpressionSyntax call =
            InvocationExpression(
                    AssertMember(
                        GenericName(Identifier(isAsync ? "ThrowsAsync" : "Throws"))
                            .WithTypeArgumentList(
                                TypeArgumentList(SingletonSeparatedList(exceptionType)))))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(lambda))));

        return ExpressionStatement(isAsync ? AwaitExpression(call) : call);
    }

    // ---------------- ASSERT TRANSLATION ----------------

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax ma
            } inv &&
            ma.Expression.ToString() == "Assert" &&
            inv.ArgumentList.Arguments.Count == 3 &&
            !IsAsyncThrowsAssert(ma.Name.Identifier.Text))
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

        var rewritten = base.VisitExpressionStatement(node);

        // Assert.ThrowsAsync returns a Task. Left unawaited it can never fail the test, so await it.
        if (_currentMethodIsAsync &&
            rewritten is ExpressionStatementSyntax statement &&
            statement.Expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax asyncMember
            } asyncCall &&
            asyncMember.Expression.ToString() == "Assert" &&
            IsAsyncThrowsAssert(asyncMember.Name.Identifier.Text))
        {
            return statement.WithExpression(AwaitExpression(asyncCall));
        }

        return rewritten;
    }

    private static bool IsAsyncThrowsAssert(string methodName)
    {
        return methodName is "ThrowsAsync" or "ThrowsExceptionAsync";
    }
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax ma ||
            ma.Expression.ToString() != "Assert")
            return base.VisitInvocationExpression(node);

        var method = ma.Name.Identifier.Text;
        var args = node.ArgumentList.Arguments;

        // MSTest passes the expected type as an argument; xUnit takes it as a type argument.
        if (method == "IsInstanceOfType" &&
            args.Count >= 2 &&
            args[1].Expression is TypeOfExpressionSyntax typeOf)
        {
            return node
                .WithExpression(
                    AssertMember(
                        GenericName(Identifier("IsType"))
                            .WithTypeArgumentList(
                                TypeArgumentList(SingletonSeparatedList(typeOf.Type)))))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(args[0])));
        }

        var target = method switch
        {
            "AreEqual" => "Equal",
            "AreNotEqual" => "NotEqual",
            "AreSame" => "Same",
            "AreNotSame" => "NotSame",
            "IsTrue" => "True",
            "IsFalse" => "False",
            "IsNull" => "Null",
            "IsNotNull" => "NotNull",
            "IsInstanceOf" => "IsType",
            "ThrowsException" => "Throws",
            "ThrowsExceptionAsync" => "ThrowsAsync",
            _ => null
        };

        if (target == null)
            return base.VisitInvocationExpression(node);

        // Only the value-comparing asserts take a trailing failure message; Throws takes a delegate.
        var hasMessageArgument = args.Count == 3 &&
                                 target is "Equal" or "NotEqual" or "Same" or "NotSame"
                                     or "True" or "False" or "Null" or "NotNull";

        if (hasMessageArgument)
            _needsOutputHelper = true;

        return node
            .WithExpression(AssertMember(Rename(ma.Name, target)))
            .WithArgumentList(
                ArgumentList(
                    hasMessageArgument
                        ? SeparatedList(new[] { args[0], args[1] })
                        : args));
    }

    private static MemberAccessExpressionSyntax AssertMember(SimpleNameSyntax name)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName("Assert"),
            name);
    }

    /// <summary>Renames an assert, keeping any type argument list (<c>ThrowsExceptionAsync&lt;T&gt;</c>).</summary>
    private static SimpleNameSyntax Rename(SimpleNameSyntax name, string newName)
    {
        return name is GenericNameSyntax generic
            ? generic.WithIdentifier(Identifier(newName))
            : IdentifierName(newName);
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

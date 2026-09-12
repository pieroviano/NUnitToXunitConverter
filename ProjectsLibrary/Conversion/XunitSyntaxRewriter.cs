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
    private bool _needsLinq;

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

        // The generated code reaches for namespaces the source file had no reason to import.
        if (_needsLinq)
            newNode = WithUsing(newNode, "System.Linq");

        // ITestOutputHelper lives in Xunit.Abstractions, not Xunit.
        if (_needsOutputHelper)
            newNode = WithUsing(newNode, "Xunit.Abstractions");

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

        // [InlineData] makes a method data-driven, and xUnit discovers those through [Theory] - [Fact]
        // alongside it is an error. MSTest always pairs [DataRow] with [TestMethod], but NUnit's [TestCase]
        // stands on its own, so there may be no [Fact] to rename and the [Theory] has to be added.
        if (HasAttribute(rewritten, "InlineData") && !HasAttribute(rewritten, "Theory"))
        {
            rewritten = HasAttribute(rewritten, "Fact")
                ? RenameAttribute(rewritten, "Fact", "Theory")
                : AddAttribute(rewritten, "Theory");
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

    private static bool HasAttribute(MethodDeclarationSyntax method, string name)
    {
        return method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() == name);
    }

    /// <summary>Renames every occurrence of an attribute, keeping its arguments and its list layout.</summary>
    private static MethodDeclarationSyntax RenameAttribute(
        MethodDeclarationSyntax method,
        string from,
        string to)
    {
        return method.ReplaceNodes(
            method.AttributeLists
                .SelectMany(list => list.Attributes)
                .Where(attribute => attribute.Name.ToString() == from),
            (original, _) => original.WithName(IdentifierName(to)));
    }

    private static MethodDeclarationSyntax AddAttribute(MethodDeclarationSyntax method, string name)
    {
        return method.WithAttributeLists(
            method.AttributeLists.Insert(
                0,
                AttributeList(SingletonSeparatedList(Attribute(IdentifierName(name))))));
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
            } inv)
        {
            var arguments = inv.ArgumentList.Arguments;

            if (ma.Expression.ToString() == "Assert" &&
                arguments.Count == 3 &&
                !IsAsyncThrowsAssert(ma.Name.Identifier.Text))
            {
                return ReportingFailureMessage(
                    (InvocationExpressionSyntax)VisitInvocationExpression(inv)!,
                    SingletonSeparatedList(arguments[2]));
            }

            // CollectionAssert takes its failure message after the arguments the xUnit rendering consumes,
            // followed by the format parameters MSTest and NUnit both allow. ITestOutputHelper.WriteLine has
            // a matching format overload, so everything past those arguments is forwarded unchanged.
            if (ma.Expression.ToString() == "CollectionAssert" &&
                CollectionAssertArities.TryGetValue(ma.Name.Identifier.Text, out var mappedArgumentCount) &&
                arguments.Count > mappedArgumentCount &&
                RewriteCollectionAssert(inv) is { } converted)
            {
                return ReportingFailureMessage(
                    converted,
                    SeparatedList(arguments.Skip(mappedArgumentCount)));
            }
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

    /// <summary>
    /// xUnit asserts carry no failure message, so the message the source passed is written to
    /// <c>ITestOutputHelper</c> from a catch clause and the failure rethrown.
    /// </summary>
    private StatementSyntax ReportingFailureMessage(
        InvocationExpressionSyntax assert,
        SeparatedSyntaxList<ArgumentSyntax> messageArguments)
    {
        _needsOutputHelper = true;

        return TryStatement(
            Block(ExpressionStatement(assert)),
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
                                    .WithArgumentList(ArgumentList(messageArguments))),
                            ThrowStatement()))),
            null);
    }
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax ma)
            return base.VisitInvocationExpression(node);

        if (ma.Expression.ToString() == "CollectionAssert")
            return RewriteCollectionAssert(node) ?? base.VisitInvocationExpression(node);

        if (ma.Expression.ToString() != "Assert")
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

    // ---------------- COLLECTION ASSERT TRANSLATION ----------------

    /// <summary>The lambda parameter the per-item asserts are written against.</summary>
    private const string CollectionItemParameter = "item";

    /// <summary>
    /// The <c>CollectionAssert</c> members that have an xUnit rendering, mapped to the number of leading
    /// arguments that rendering consumes; anything the source passes beyond them is the failure message.
    /// Members absent from this table - <c>AreNotEquivalent</c>, <c>IsNotSubsetOf</c>, <c>IsOrdered</c> -
    /// have no xUnit counterpart, so they are left alone and the converted project stops compiling on them
    /// rather than silently asserting something weaker.
    /// </summary>
    private static readonly Dictionary<string, int> CollectionAssertArities = new()
    {
        ["AreEqual"] = 2,
        ["AreNotEqual"] = 2,
        ["AreEquivalent"] = 2,
        ["Contains"] = 2,
        ["DoesNotContain"] = 2,
        ["IsEmpty"] = 1,
        ["IsNotEmpty"] = 1,
        ["AllItemsAreNotNull"] = 1,
        ["AllItemsAreUnique"] = 1,
        ["AllItemsAreInstancesOfType"] = 2,
        ["IsSubsetOf"] = 2
    };

    /// <summary>
    /// Translates a single <c>CollectionAssert</c> call, or returns <see langword="null"/> when the member
    /// has no xUnit rendering and the call should be left untouched.
    /// </summary>
    private InvocationExpressionSyntax? RewriteCollectionAssert(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax ma ||
            ma.Expression.ToString() != "CollectionAssert")
            return null;

        var method = ma.Name.Identifier.Text;

        if (!CollectionAssertArities.TryGetValue(method, out var mappedArgumentCount) ||
            node.ArgumentList.Arguments.Count < mappedArgumentCount)
            return null;

        var args = node.ArgumentList.Arguments;

        switch (method)
        {
            case "AreEqual":
                return AssertCall("Equal", args[0], args[1]);

            case "AreNotEqual":
                return AssertCall("NotEqual", args[0], args[1]);

            // Assert.Equivalent compares without regard to order, which is what AreEquivalent means.
            case "AreEquivalent":
                return AssertCall("Equivalent", args[0], args[1]);

            // CollectionAssert names the collection first, xUnit names the element first.
            case "Contains":
                return AssertCall("Contains", args[1], args[0]);

            case "DoesNotContain":
                return AssertCall("DoesNotContain", args[1], args[0]);

            case "IsEmpty":
                return AssertCall("Empty", args[0]);

            case "IsNotEmpty":
                return AssertCall("NotEmpty", args[0]);

            case "AllItemsAreNotNull":
                return AssertAll(args[0], AssertCall("NotNull", CollectionItem()));

            // The expected type becomes a type argument, so it has to be spelled out at the call site; a
            // Type-valued expression carries no syntax to lift and is left untouched.
            case "AllItemsAreInstancesOfType":
                return args[1].Expression is TypeOfExpressionSyntax typeOf
                    ? AssertAll(
                        args[0],
                        AssertCall(GenericAssert("IsType", typeOf.Type), CollectionItem()))
                    : null;

            case "IsSubsetOf":
                return AssertAll(args[0], AssertCall("Contains", CollectionItem(), args[1]));

            // xUnit has no uniqueness assert. Note this evaluates the collection expression twice, so a
            // side-effecting argument changes meaning.
            case "AllItemsAreUnique":
                _needsLinq = true;

                return AssertCall(
                    "Equal",
                    Argument(Fluent(Fluent(args[0].Expression, "Distinct"), "Count")),
                    Argument(Fluent(args[0].Expression, "Count")));

            default:
                return null;
        }
    }

    /// <summary>Builds <c>Assert.All(collection, item =&gt; assert)</c>.</summary>
    private static InvocationExpressionSyntax AssertAll(
        ArgumentSyntax collection,
        ExpressionSyntax perItemAssert)
    {
        return AssertCall(
            "All",
            collection,
            Argument(
                SimpleLambdaExpression(Parameter(Identifier(CollectionItemParameter)))
                    .WithExpressionBody(perItemAssert)));
    }

    private static ArgumentSyntax CollectionItem()
    {
        return Argument(IdentifierName(CollectionItemParameter));
    }

    private static InvocationExpressionSyntax AssertCall(string name, params ArgumentSyntax[] arguments)
    {
        return AssertCall(IdentifierName(name), arguments);
    }

    private static InvocationExpressionSyntax AssertCall(
        SimpleNameSyntax name,
        params ArgumentSyntax[] arguments)
    {
        return InvocationExpression(AssertMember(name))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));
    }

    private static GenericNameSyntax GenericAssert(string name, TypeSyntax typeArgument)
    {
        return GenericName(Identifier(name))
            .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(typeArgument)));
    }

    /// <summary>Builds <c>receiver.Name()</c>, for the LINQ calls the collection asserts expand into.</summary>
    private static InvocationExpressionSyntax Fluent(ExpressionSyntax receiver, string name)
    {
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                IdentifierName(name)));
    }

    // ---------------- HELPERS ----------------

    private static CompilationUnitSyntax WithUsing(CompilationUnitSyntax node, string name)
    {
        return node.Usings.Any(u => u.Name?.ToString() == name)
            ? node
            : node.WithUsings(node.Usings.Add(UsingDirective(ParseName(name))));
    }

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

using System.Diagnostics;
using ConversionClassLibrary;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ProjectsLibrary.Conversion;

public class XunitSyntaxRewriter : CSharpSyntaxRewriter
{
    private bool _currentMethodIsAsync;

    // Per file.
    private bool _needsOutputHelper;
    private bool _needsLinq;
    private Framework _sourceFramework = Framework.Unknown;
    private readonly List<MemberDeclarationSyntax> _generatedTypes = [];

    // Per class, reset on entry to every class declaration. Left set, they grafted one class's setup and
    // teardown onto the next class in the same file - ordinary in both frameworks, and it did not compile.
    private readonly Dictionary<Lifecycle, List<StatementSyntax>> _lifecycleBodies = [];
    private string? _testClassName;

    /// <summary>
    /// Shared by every file of one project, so assembly-wide setup declared in one file can be joined by the
    /// test classes in the others.
    /// </summary>
    public OneTimeSetUpContext OneTimeSetUpContext { get; set; } = new OneTimeSetUpContext();

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

        // Lifecycle hooks become constructors, Dispose methods and fixture classes, so their attributes go.
        if (LifecycleMethods.AttributeNames.Contains(name))
            return null;

        // Re-expressed as Skip, Timeout, Trait or MemberData on the declaration itself.
        if (AttributeTranslator.Consumed.Contains(name))
            return null;

        // Assembly-level parallelism settings, re-expressed as [assembly: CollectionBehavior] where xUnit
        // has an equivalent and dropped where it has none.
        if (AssemblySettings.Names.Contains(name))
            return null;

        return name switch
        {
            // NUnit
            "Test" => node.WithName(IdentifierName("Fact")),
            "TestCase" => node.WithName(IdentifierName("InlineData")),
            // A parameterised [TestFixture(1)] has no xUnit equivalent. Dropping it silently discarded the
            // parameterisation, so it is left in place to fail visibly instead.
            "TestFixture" => node.ArgumentList == null ? null : base.VisitAttribute(node),
            "SetUpFixture" => null,
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

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var enclosingClassName = _testClassName;
        var enclosingNeedsOutputHelper = _needsOutputHelper;

        _lifecycleBodies.Clear();
        _needsOutputHelper = false;
        _testClassName = node.Identifier.Text;

        // Recognised on the original class, where the attributes still are; removed from the rewritten
        // members by signature, because those are different node instances.
        var lifecycle = LifecycleMethods.Find(node);

        var newNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

        var members = new List<MemberDeclarationSyntax>();

        foreach (var member in newNode.Members)
        {
            // The body is taken from the rewritten method, so asserts inside a setup are converted too.
            if (member is MethodDeclarationSyntax method &&
                lifecycle.TryGetValue(LifecycleMethods.SignatureOf(method), out var kind))
            {
                if (method.Body != null)
                    Bodies(kind).AddRange(method.Body.Statements);

                continue;
            }

            // MSTest's TestContext is replaced by ITestOutputHelper, so its property goes with it.
            if (member is PropertyDeclarationSyntax property && property.Type.ToString() == "TestContext")
                continue;

            members.Add(member);
        }

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

        // ---- Constructor (per-test setup) ----
        // Presence of the hook decides, not whether its body happens to be empty: an empty [OneTimeSetUp]
        // still means the class had one, and dropping its fixture would change what the class declares.
        var instanceSetUp = StatementsOf(Lifecycle.InstanceSetUp);

        if (Has(Lifecycle.InstanceSetUp) || _needsOutputHelper)
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

            ctorBody.AddRange(instanceSetUp);

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

        // ---- Dispose (per-test teardown) ----
        var instanceTearDown = StatementsOf(Lifecycle.InstanceTearDown);

        if (Has(Lifecycle.InstanceTearDown))
        {
            members.Add(
                MethodDeclaration(
                    PredefinedType(Token(SyntaxKind.VoidKeyword)), "Dispose")
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithBody(Block(instanceTearDown)));

            newNode = AddInterface(newNode, "System.IDisposable");
        }

        // ---- Per-class fixture ----
        var classSetUp = StatementsOf(Lifecycle.ClassSetUp);
        var classTearDown = StatementsOf(Lifecycle.ClassTearDown);

        if (Has(Lifecycle.ClassSetUp) || Has(Lifecycle.ClassTearDown))
        {
            var fixtureName = $"{_testClassName}Fixture";

            OneTimeSetUpContext.FixtureClassName ??= fixtureName;
            _generatedTypes.Add(FixtureBuilder.Fixture(fixtureName, classSetUp, classTearDown));

            newNode = AddInterface(newNode, $"IClassFixture<{fixtureName}>");
        }

        // ---- Assembly-wide fixture ----
        var assemblySetUp = StatementsOf(Lifecycle.AssemblySetUp);
        var assemblyTearDown = StatementsOf(Lifecycle.AssemblyTearDown);
        var heldAssemblyHooks = Has(Lifecycle.AssemblySetUp) || Has(Lifecycle.AssemblyTearDown);

        // One assembly, one fixture. A project declaring assembly-wide setup twice - MSTest's
        // [AssemblyInitialize] in one file and NUnit's [SetUpFixture] in another - would otherwise emit two
        // types of the same name, so the second is reported rather than silently merged or duplicated.
        if (heldAssemblyHooks && OneTimeSetUpContext.AssemblyCollectionEmitted)
        {
            LoggerFactoryContainer.Instance.LoggerFactory.Warn(
                $"{_testClassName} declares assembly-wide setup, but the project already has some. " +
                "Its body has been left out of the generated fixture and needs merging by hand.");

            heldAssemblyHooks = false;
        }
        else if (heldAssemblyHooks)
        {
            _generatedTypes.Add(FixtureBuilder.Fixture(
                FixtureBuilder.AssemblyFixtureName, assemblySetUp, assemblyTearDown));

            _generatedTypes.Add(FixtureBuilder.AssemblyCollectionDefinition());
            OneTimeSetUpContext.AssemblyCollectionEmitted = true;
        }

        newNode = newNode.WithMembers(List(members));

        // [Category] and friends on the class itself become class-level traits.
        foreach (var trait in AttributeTranslator.Traits(node.AttributeLists))
            newNode = newNode.WithAttributeLists(newNode.AttributeLists.Add(trait));

        _testClassName = enclosingClassName;
        _needsOutputHelper |= enclosingNeedsOutputHelper;
        _lifecycleBodies.Clear();

        // A class that held nothing but the assembly-wide hooks has been replaced by the generated fixture.
        if (heldAssemblyHooks && members.Count == 0)
            return null;

        // Everything else joins the collection: it is the only way xUnit shares one fixture instance across
        // every test class, which is what [AssemblyInitialize] and [SetUpFixture] mean.
        if (OneTimeSetUpContext.ProjectHasAssemblyFixture && !HasClassAttribute(newNode, "Collection"))
        {
            newNode = newNode.WithAttributeLists(
                newNode.AttributeLists.Add(FixtureBuilder.AssemblyCollectionAttribute()));
        }

        return newNode;
    }

    /// <summary>The hook's statements, creating the entry - which is what records that the hook exists.</summary>
    private List<StatementSyntax> Bodies(Lifecycle lifecycle)
    {
        if (!_lifecycleBodies.TryGetValue(lifecycle, out var statements))
            _lifecycleBodies[lifecycle] = statements = [];

        return statements;
    }

    private bool Has(Lifecycle lifecycle)
    {
        return _lifecycleBodies.ContainsKey(lifecycle);
    }

    /// <summary>Reads without recording, so asking does not make the hook appear to exist.</summary>
    private IReadOnlyList<StatementSyntax> StatementsOf(Lifecycle lifecycle)
    {
        return _lifecycleBodies.TryGetValue(lifecycle, out var statements) ? statements : [];
    }

    private static bool HasClassAttribute(ClassDeclarationSyntax node, string name)
    {
        return node.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() == name);
    }

    // ---------------- FIXTURE GENERATION ----------------

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
    {
        // Settled before the members are visited: StringAssert has the same member names in both
        // frameworks with the arguments the other way round, so the translation needs to know which is which.
        _sourceFramework = SourceFramework.Of(node);

        var newNode = (CompilationUnitSyntax)base.VisitCompilationUnit(node)!;

        newNode = WithGeneratedTypes(newNode);

        var collectionBehavior = AssemblySettings.CollectionBehavior(node.AttributeLists);

        if (collectionBehavior != null)
        {
            newNode = WithUsing(newNode, "Xunit");
            newNode = newNode.WithAttributeLists(newNode.AttributeLists.Add(collectionBehavior));
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
                Block(ThrowsAssert(expectedException, rewritten.Body, isAsync, AllowsDerived(node))));
        }

        // [TestCaseSource]/[DynamicData] were dropped by VisitAttribute; their [MemberData] goes on here.
        foreach (var memberData in AttributeTranslator.MemberData(node.AttributeLists))
            rewritten = rewritten.WithAttributeLists(rewritten.AttributeLists.Add(memberData));

        // [Category], [Priority], [Owner] and the rest become traits.
        foreach (var trait in AttributeTranslator.Traits(node.AttributeLists))
            rewritten = rewritten.WithAttributeLists(rewritten.AttributeLists.Add(trait));

        // An NUnit [TestCase(..., ExpectedResult = x)] returns the value under test; xUnit theories are void,
        // so the expectation becomes a parameter and an assert.
        rewritten = ExpectedResult.Rewrite(rewritten);

        // [InlineData]/[MemberData] make a method data-driven, and xUnit discovers those through [Theory] -
        // [Fact] alongside one is an error. MSTest always pairs [DataRow] with [TestMethod], but NUnit's
        // [TestCase] stands on its own, so there may be no [Fact] to rename and [Theory] has to be added.
        if ((HasAttribute(rewritten, "InlineData") || HasAttribute(rewritten, "MemberData")) &&
            !HasAttribute(rewritten, "Theory"))
        {
            rewritten = HasAttribute(rewritten, "Fact")
                ? RenameAttribute(rewritten, "Fact", "Theory")
                : AddAttribute(rewritten, "Theory");
        }

        // Skip and Timeout are named arguments of the [Fact]/[Theory] that now exists.
        rewritten = rewritten.WithAttributeLists(
            AttributeTranslator.WithFactArguments(
                rewritten.AttributeLists,
                AttributeTranslator.SkipReason(node.AttributeLists),
                AttributeTranslator.Timeout(node.AttributeLists)));

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
    /// <summary>
    /// Whether <c>[ExpectedException(typeof(T), AllowDerivedTypes = true)]</c> was asked for. It is the
    /// difference between xUnit's <c>Assert.Throws&lt;T&gt;</c>, which demands that exact type, and
    /// <c>Assert.ThrowsAny&lt;T&gt;</c>, which accepts a subclass.
    /// </summary>
    private static bool AllowsDerived(MethodDeclarationSyntax node)
    {
        return node.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute => attribute.Name.ToString() == "ExpectedException")
            .SelectMany(attribute => attribute.ArgumentList?.Arguments ?? default)
            .Any(argument =>
                argument.NameEquals?.Name.Identifier.Text == "AllowDerivedTypes" &&
                argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression));
    }

    private static StatementSyntax ThrowsAssert(
        TypeSyntax exceptionType,
        BlockSyntax body,
        bool isAsync,
        bool allowDerived = false)
    {
        var lambda = ParenthesizedLambdaExpression().WithBlock(body);

        if (isAsync)
            lambda = lambda.WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword));

        ExpressionSyntax call =
            InvocationExpression(
                    AssertMember(
                        GenericName(Identifier(ThrowsName(isAsync, allowDerived)))
                            .WithTypeArgumentList(
                                TypeArgumentList(SingletonSeparatedList(exceptionType)))))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(lambda))));

        return ExpressionStatement(isAsync ? AwaitExpression(call) : call);
    }

    // ---------------- ASSERT TRANSLATION ----------------

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        // Replacement nodes are built from scratch and so carry no trivia of their own. Without this the
        // comment or #pragma attached to the statement being replaced disappears with it.
        return RewriteStatement(node) is { } rewritten && !ReferenceEquals(rewritten, node)
            ? rewritten.WithTriviaFrom(node)
            : node;
    }

    private SyntaxNode? RewriteStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax ma
            } inv)
        {
            var arguments = inv.ArgumentList.Arguments;

            if (ma.Expression.ToString() == "Assert" &&
                arguments.Count == 3 &&
                IsFailureMessage(arguments[2].Expression) &&
                !IsAsyncThrowsAssert(ma.Name.Identifier.Text))
            {
                return ReportingFailureMessage(
                    (InvocationExpressionSyntax)RewriteInvocation(inv)!,
                    SingletonSeparatedList(arguments[2]));
            }

            // CollectionAssert takes its failure message after the arguments the xUnit rendering consumes,
            // followed by the format parameters MSTest and NUnit both allow. ITestOutputHelper.WriteLine has
            // a matching format overload, so everything past those arguments is forwarded unchanged.
            if (ma.Expression.ToString() == "CollectionAssert" &&
                CollectionAssertArities.TryGetValue(ma.Name.Identifier.Text, out var mappedArgumentCount) &&
                arguments.Count > mappedArgumentCount &&
                IsFailureMessage(arguments[mappedArgumentCount].Expression) &&
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

    private static string ThrowsName(bool isAsync, bool allowDerived)
    {
        if (isAsync)
            return allowDerived ? "ThrowsAnyAsync" : "ThrowsAsync";

        return allowDerived ? "ThrowsAny" : "Throws";
    }

    private static bool IsAsyncThrowsAssert(string methodName)
    {
        return methodName is "ThrowsAsync" or "ThrowsAnyAsync" or "ThrowsExceptionAsync";
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
        return RewriteInvocation(node) is { } rewritten && !ReferenceEquals(rewritten, node)
            ? rewritten.WithTriviaFrom(node)
            : node;
    }

    private SyntaxNode? RewriteInvocation(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax ma)
            return base.VisitInvocationExpression(node);

        if (ma.Expression.ToString() == "CollectionAssert")
            return RewriteCollectionAssert(node) ?? base.VisitInvocationExpression(node);

        if (ma.Expression.ToString() == "StringAssert")
            return StringAssertTranslator.Rewrite(node, _sourceFramework) ??
                   base.VisitInvocationExpression(node);

        // MSTest's TestContext exists to write to the test's output, which is exactly ITestOutputHelper.
        if (ma.Expression.ToString() == "TestContext" &&
            ma.Name.Identifier.Text is "WriteLine" or "Write")
        {
            _needsOutputHelper = true;

            return node.WithExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("_output"),
                    IdentifierName("WriteLine")));
        }

        if (ma.Expression.ToString() == "Assert" && ma.Name.Identifier.Text == "That")
            return ConstraintTranslator.Rewrite(node) ?? base.VisitInvocationExpression(node);

        if (ma.Expression.ToString() != "Assert")
            return base.VisitInvocationExpression(node);

        var method = ma.Name.Identifier.Text;
        var args = node.ArgumentList.Arguments;

        // Both frameworks have AreEqual(expected, actual, delta) for floating point. Treating the delta as a
        // failure message produced _output.WriteLine(0.01), which does not compile and lost the tolerance.
        if (method == "AreEqual" && args.Count == 3 && !IsFailureMessage(args[2].Expression))
        {
            var precision = PrecisionOf(args[2].Expression);

            return precision == null
                ? base.VisitInvocationExpression(node)
                : node
                    .WithExpression(AssertMember(IdentifierName("Equal")))
                    .WithArgumentList(
                        ArgumentList(SeparatedList(new[] { args[0], args[1], Argument(precision) })));
        }

        // NUnit's ordering and sign asserts have no xUnit counterpart, so the comparison is spelled out.
        var comparison = method switch
        {
            "Greater" => SyntaxKind.GreaterThanExpression,
            "GreaterOrEqual" => SyntaxKind.GreaterThanOrEqualExpression,
            "Less" => SyntaxKind.LessThanExpression,
            "LessOrEqual" => SyntaxKind.LessThanOrEqualExpression,
            _ => SyntaxKind.None
        };

        if (comparison != SyntaxKind.None && args.Count >= 2)
        {
            return AssertCall(
                "True",
                Argument(BinaryExpression(comparison, args[0].Expression, args[1].Expression)));
        }

        var sign = method switch
        {
            "Positive" => SyntaxKind.GreaterThanExpression,
            "Negative" => SyntaxKind.LessThanExpression,
            _ => SyntaxKind.None
        };

        if (sign != SyntaxKind.None && args.Count >= 1)
        {
            return AssertCall(
                "True",
                Argument(BinaryExpression(sign, args[0].Expression, Zero())));
        }

        if (method is "Zero" or "NotZero" && args.Count >= 1)
            return AssertCall(method == "Zero" ? "Equal" : "NotEqual", Argument(Zero()), args[0]);

        // MSTest passes the expected type as an argument; xUnit takes it as a type argument.
        if (method is "IsInstanceOfType" or "IsNotInstanceOfType" &&
            args.Count >= 2 &&
            args[1].Expression is TypeOfExpressionSyntax notInstanceType)
        {
            return AssertCall(
                GenericAssert(method == "IsInstanceOfType" ? "IsType" : "IsNotType", notInstanceType.Type),
                args[0]);
        }

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
            "IsNotInstanceOf" => "IsNotType",
            "IsEmpty" => "Empty",
            "IsNotEmpty" => "NotEmpty",
            "ThrowsException" => "Throws",
            "ThrowsExceptionAsync" => "ThrowsAsync",
            _ => null
        };

        if (target == null)
            return base.VisitInvocationExpression(node);

        // Only the value-comparing asserts take a trailing failure message; Throws takes a delegate.
        var hasMessageArgument = args.Count == 3 &&
                                 IsFailureMessage(args[2].Expression) &&
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

    /// <summary>
    /// xUnit compares doubles to a number of decimal places, not to a tolerance, so only a delta that is a
    /// clean power of ten converts exactly. Anything else - a variable, or 0.5 - returns null and the call is
    /// left alone rather than silently given a different tolerance.
    /// </summary>
    private static ExpressionSyntax? PrecisionOf(ExpressionSyntax delta)
    {
        if (delta is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return null;
        }

        var text = literal.Token.Text.TrimEnd('d', 'D', 'f', 'F', 'm', 'M');
        var separator = text.IndexOf('.');

        if (separator < 0)
            return null;

        var fraction = text[(separator + 1)..];

        // 0.01 is two places, 0.001 is three; 0.05 is not expressible and is refused.
        if (text[..separator] != "0" || fraction.Length == 0 || fraction[^1] != '1' ||
            fraction[..^1].Any(digit => digit != '0'))
        {
            return null;
        }

        return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(fraction.Length));
    }

    private static LiteralExpressionSyntax Zero()
    {
        return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0));
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

    /// <summary>The lambda parameter the uniqueness check groups into.</summary>
    private const string GroupParameter = "group";

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

        // Both frameworks also offer IComparer overloads in the same position as the failure message. An
        // extra argument that is not recognisably a string is assumed to be one of those: it carries
        // comparison semantics no xUnit assert expresses, so the call is left for a human.
        if (args.Count > mappedArgumentCount && !IsFailureMessage(args[mappedArgumentCount].Expression))
            return null;

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

            // xUnit has no uniqueness assert. Grouping, then asserting that no group holds more than one
            // member, says the same thing while naming the collection once - so a side-effecting argument
            // such as AllItemsAreUnique(GetItems()) keeps its meaning.
            case "AllItemsAreUnique":
                _needsLinq = true;

                return AssertCall("Empty", Argument(NoDuplicateGroups(args[0].Expression)));

            default:
                return null;
        }
    }

    /// <summary>Builds <c>Assert.All(collection, item =&gt; assert)</c>.</summary>
    private static InvocationExpressionSyntax AssertAll(
        ArgumentSyntax collection,
        ExpressionSyntax perItemAssert)
    {
        return AssertCall("All", collection, Argument(Lambda(CollectionItemParameter, perItemAssert)));
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

    /// <summary>
    /// Builds <c>collection.GroupBy(item =&gt; item).Where(group =&gt; group.Count() &gt; 1)</c> - the groups
    /// that make a collection non-unique, with <paramref name="collection"/> written exactly once.
    /// </summary>
    private static InvocationExpressionSyntax NoDuplicateGroups(ExpressionSyntax collection)
    {
        var grouped = Fluent(
            collection,
            "GroupBy",
            Argument(Lambda(CollectionItemParameter, IdentifierName(CollectionItemParameter))));

        var duplicated = BinaryExpression(
            SyntaxKind.GreaterThanExpression,
            Fluent(IdentifierName(GroupParameter), "Count"),
            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)));

        return Fluent(grouped, "Where", Argument(Lambda(GroupParameter, duplicated)));
    }

    /// <summary>Builds <c>receiver.Name(arguments)</c>, for the LINQ calls the collection asserts expand into.</summary>
    private static InvocationExpressionSyntax Fluent(
        ExpressionSyntax receiver,
        string name,
        params ArgumentSyntax[] arguments)
    {
        return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    IdentifierName(name)))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));
    }

    private static SimpleLambdaExpressionSyntax Lambda(string parameter, ExpressionSyntax body)
    {
        return SimpleLambdaExpression(Parameter(Identifier(parameter))).WithExpressionBody(body);
    }

    /// <summary>
    /// Whether an argument can be taken for a failure message. The rewriter is syntax-only, so this is the
    /// most that can be said without type information: an expression built out of string literals is
    /// certainly text, and anything else might be the comparer of an <c>IComparer</c> overload.
    /// </summary>
    private static bool IsFailureMessage(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
            InterpolatedStringExpressionSyntax => true,
            // "prefix " + value, and the nested concatenations that build up from it.
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
                IsFailureMessage(binary.Left) || IsFailureMessage(binary.Right),
            ParenthesizedExpressionSyntax parenthesized => IsFailureMessage(parenthesized.Expression),
            _ => false
        };
    }

    // ---------------- HELPERS ----------------

    /// <summary>
    /// Adds the generated fixtures. A block-scoped namespace has to be entered to add them: appended to the
    /// compilation unit they would land outside it, in a different namespace from the class whose
    /// <c>IClassFixture&lt;&gt;</c> names them, and would not resolve.
    /// </summary>
    private CompilationUnitSyntax WithGeneratedTypes(CompilationUnitSyntax node)
    {
        if (_generatedTypes.Count == 0)
            return node;

        var generated = _generatedTypes.ToArray();
        _generatedTypes.Clear();

        var blockNamespace = node.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

        return blockNamespace == null
            ? node.AddMembers(generated)
            : node.ReplaceNode(blockNamespace, blockNamespace.AddMembers(generated));
    }

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

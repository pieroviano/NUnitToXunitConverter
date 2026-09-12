using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectsLibrary.Conversion;

/// <summary>The xUnit shape a framework lifecycle hook has to end up in.</summary>
public enum Lifecycle
{
    /// <summary>Runs before every test: xUnit's constructor.</summary>
    InstanceSetUp,

    /// <summary>Runs after every test: xUnit's <c>Dispose</c>.</summary>
    InstanceTearDown,

    /// <summary>Runs once per class: the constructor of a generated <c>&lt;Class&gt;Fixture</c>.</summary>
    ClassSetUp,

    /// <summary>Runs once per class: <c>Dispose</c> on that fixture.</summary>
    ClassTearDown,

    /// <summary>Runs once per assembly: the constructor of a shared collection fixture.</summary>
    AssemblySetUp,

    /// <summary>Runs once per assembly: <c>Dispose</c> on that collection fixture.</summary>
    AssemblyTearDown
}

/// <summary>
/// Finds the lifecycle hooks of a class and identifies them by signature rather than by node.
/// </summary>
/// <remarks>
/// Signature, not node reference: the hooks have to be recognised on the original class - that is where the
/// attributes still are - but removed from the *rewritten* member list, whose nodes are different instances.
/// Comparing references across that boundary silently never matched, which left every hook in place with its
/// framework attribute intact while its body was also copied into the generated constructor.
/// </remarks>
public static class LifecycleMethods
{
    /// <summary>
    /// Attributes that mark a hook, and the shape they map to. <c>OneTimeSetUp</c> and <c>OneTimeTearDown</c>
    /// are deliberately absent: their scope depends on the class they sit in, so they are resolved by
    /// <see cref="Find"/>.
    /// </summary>
    private static readonly Dictionary<string, Lifecycle> FixedScope = new()
    {
        ["SetUp"] = Lifecycle.InstanceSetUp,
        ["TestInitialize"] = Lifecycle.InstanceSetUp,
        ["TearDown"] = Lifecycle.InstanceTearDown,
        ["TestCleanup"] = Lifecycle.InstanceTearDown,
        ["ClassInitialize"] = Lifecycle.ClassSetUp,
        ["ClassCleanup"] = Lifecycle.ClassTearDown,
        ["AssemblyInitialize"] = Lifecycle.AssemblySetUp,
        ["AssemblyCleanup"] = Lifecycle.AssemblyTearDown
    };

    /// <summary>Every attribute name that marks a lifecycle hook, whatever its scope.</summary>
    public static IEnumerable<string> AttributeNames =>
        FixedScope.Keys.Concat(["OneTimeSetUp", "OneTimeTearDown"]);

    /// <summary>
    /// The lifecycle hooks of <paramref name="node"/>, keyed by <see cref="SignatureOf"/>. A class marked
    /// <c>[SetUpFixture]</c> holds NUnit's namespace- or assembly-wide hooks, so its <c>[OneTimeSetUp]</c>
    /// means something different from the same attribute inside a <c>[TestFixture]</c>.
    /// </summary>
    public static Dictionary<string, Lifecycle> Find(ClassDeclarationSyntax node)
    {
        var oneTimeIsAssemblyWide = node.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() == "SetUpFixture");

        var found = new Dictionary<string, Lifecycle>();

        foreach (var method in node.Members.OfType<MethodDeclarationSyntax>())
        {
            foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
            {
                var name = attribute.Name.ToString();

                Lifecycle? lifecycle = name switch
                {
                    "OneTimeSetUp" => oneTimeIsAssemblyWide ? Lifecycle.AssemblySetUp : Lifecycle.ClassSetUp,
                    "OneTimeTearDown" => oneTimeIsAssemblyWide
                        ? Lifecycle.AssemblyTearDown
                        : Lifecycle.ClassTearDown,
                    _ => FixedScope.TryGetValue(name, out var fixedScope) ? fixedScope : null
                };

                if (lifecycle != null)
                {
                    found[SignatureOf(method)] = lifecycle.Value;

                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Name plus parameter count. Enough to tell a hook from an overload of the same name, and stable across
    /// the rewrite, which is the whole point.
    /// </summary>
    public static string SignatureOf(MethodDeclarationSyntax method)
    {
        return $"{method.Identifier.Text}/{method.ParameterList.Parameters.Count}";
    }
}

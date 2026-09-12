using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using ProjectsLibrary;
using ProjectsLibrary.Conversion;

namespace MsOrNUnitToXunitConverter.Tests;

/// <summary>
/// SpecFlow runs on top of a unit test provider rather than being one, so a SpecFlow project is retargeted:
/// the provider package and the configured provider name change, and the bindings stay as they are.
/// </summary>
public class SpecFlowRetargetTests
{
    private const string Csproj = @"C:\proj\Acme.Specs\Acme.Specs.csproj";

    // ---------------- the provider package ----------------

    [Theory]
    [InlineData("SpecFlow.NUnit")]
    [InlineData("SpecFlow.MsTest")]
    public void The_Provider_Package_Is_Retargeted_And_Keeps_Its_Version(string provider)
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText(Csproj).Returns($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="SpecFlow" Version="3.9.74" />
                <PackageReference Include="{provider}" Version="3.9.74" />
                <PackageReference Include="NUnit" Version="3.13.3" />
              </ItemGroup>
            </Project>
            """);

        string? written = null;
        file.When(f => f.WriteAllText(Csproj, Arg.Any<string>()))
            .Do(call => written = call.ArgAt<string>(1));

        new TestPackagesRewriter { File = file }.RewritePackageReferences(Csproj);

        Assert.Contains(@"Include=""SpecFlow.xUnit"" Version=""3.9.74""", written);
        Assert.DoesNotContain(provider, written);

        // SpecFlow itself has to survive; dropping it with the rest would take the framework with it.
        Assert.Contains(@"Include=""SpecFlow"" Version=""3.9.74""", written);

        // The underlying unit test framework is still swapped for the xUnit set.
        Assert.Contains(@"Include=""xunit""", written);
        Assert.DoesNotContain(@"Include=""NUnit""", written);
    }

    [Fact]
    public void A_Project_Whose_Only_Framework_Reference_Is_SpecFlow_Still_Counts_As_A_Test_Project()
    {
        var file = Substitute.For<IFile>();
        file.ReadAllText(Csproj).Returns("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="SpecFlow.NUnit" Version="3.9.74" />
              </ItemGroup>
            </Project>
            """);

        // The solution-wide gate would otherwise skip it entirely.
        Assert.True(new TestPackagesRewriter { File = file }.ReferencesTestPackages(Csproj));
    }

    [Fact]
    public void A_Second_Run_Over_The_Converters_Own_Output_Changes_Nothing()
    {
        var file = Substitute.For<IFile>();
        var sut = new TestPackagesRewriter { File = file };

        var current = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="SpecFlow" Version="3.9.74" />
                <PackageReference Include="SpecFlow.NUnit" Version="3.9.74" />
                <PackageReference Include="NUnit" Version="3.13.3" />
              </ItemGroup>
            </Project>
            """;

        file.ReadAllText(Csproj).Returns(_ => current);
        file.When(f => f.WriteAllText(Csproj, Arg.Any<string>()))
            .Do(call => current = call.ArgAt<string>(1));

        Assert.True(sut.RewritePackageReferences(Csproj));

        // Re-running has to be a no-op, or the loop that re-runs the converter is not safe.
        Assert.False(sut.RewritePackageReferences(Csproj));
        Assert.Contains(@"Include=""SpecFlow.xUnit""", current);
    }

    // ---------------- generated code-behind ----------------

    [Theory]
    [InlineData(@"C:\proj\Features\Login.feature.cs", true)]
    [InlineData(@"C:\proj\Features\Login.feature", false)]
    [InlineData(@"C:\proj\Steps\LoginSteps.cs", false)]
    public void Generated_Feature_Code_Is_Recognised(string path, bool expected)
    {
        Assert.Equal(expected, SpecFlow.IsGeneratedFeatureCode(path));
    }

    [Fact]
    public void Generated_Feature_Code_Is_Deleted_So_The_Build_Regenerates_It()
    {
        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        path.GetFullPath(Csproj).Returns(Csproj);
        path.GetDirectoryName(Csproj).Returns(@"C:\proj\Acme.Specs");
        path.Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => $"{call.ArgAt<string>(0)}\\{call.ArgAt<string>(1)}");

        file.Exists(Arg.Any<string>()).Returns(false);
        directory.GetFiles(Arg.Any<string>(), "*.feature.cs", Arg.Any<SearchOption>())
            .Returns([@"C:\proj\Acme.Specs\Features\Login.feature.cs"]);

        var sut = new SpecFlowRetargetService { File = file, Path = path, Directory = directory };

        var changed = sut.Retarget(Csproj);

        // A stale code-behind shaped for the old provider is what actually breaks the build after a retarget.
        file.Received(1).Delete(@"C:\proj\Acme.Specs\Features\Login.feature.cs");
        Assert.Contains(@"C:\proj\Acme.Specs\Features\Login.feature.cs", changed);
    }

    [Fact]
    public void Generated_Feature_Code_Is_Never_Offered_For_Rewriting()
    {
        // ProjectScanner loads the csproj from disk itself, so this one needs real files.
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MsOrNUnitToXunitConverter.Tests",
            Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Features"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Steps"));

        var csproj = System.IO.Path.Combine(root, "Acme.Specs.csproj");
        var generated = System.IO.Path.Combine(root, "Features", "Login.feature.cs");
        var steps = System.IO.Path.Combine(root, "Steps", "LoginSteps.cs");

        try
        {
            System.IO.File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            System.IO.File.WriteAllText(generated, "// <auto-generated />");
            System.IO.File.WriteAllText(steps, "[Binding] public class LoginSteps { }");

            var found = new ProjectScanner().GetCsFiles(csproj).ToList();

            Assert.Contains(steps, found);
            Assert.DoesNotContain(generated, found);
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    // ---------------- configuration ----------------

    [Fact]
    public void The_Json_Provider_Is_Retargeted_And_The_Rest_Of_The_File_Is_Untouched()
    {
        var retargeted = SpecFlow.RetargetJson("""
            {
              "unitTestProvider": {
                "name": "nunit"
              },
              "bindingCulture": {
                "name": "en-GB"
              }
            }
            """);

        Assert.NotNull(retargeted);
        Assert.Contains("\"name\": \"xunit\"", retargeted);
        Assert.DoesNotContain("nunit", retargeted);

        // Edited as text, so formatting, key order and any other setting survive.
        Assert.Contains("\"bindingCulture\"", retargeted);
        Assert.Contains("\"en-GB\"", retargeted);
    }

    [Fact]
    public void A_Json_Already_On_Xunit_Reports_No_Change()
    {
        Assert.Null(SpecFlow.RetargetJson("""{ "unitTestProvider": { "name": "xunit" } }"""));
    }

    [Fact]
    public void A_Json_Without_A_Provider_Is_Left_Alone()
    {
        // SpecFlow infers the provider from the package when the setting is absent.
        Assert.Null(SpecFlow.RetargetJson("""{ "bindingCulture": { "name": "en-GB" } }"""));
    }

    [Fact]
    public void The_App_Config_Provider_Is_Retargeted()
    {
        var configPath = @"C:\proj\Acme.Specs\App.config";

        var file = Substitute.For<IFile>();
        var path = Substitute.For<IPath>();
        var directory = Substitute.For<IDirectory>();

        path.GetFullPath(Csproj).Returns(Csproj);
        path.GetDirectoryName(Csproj).Returns(@"C:\proj\Acme.Specs");
        path.Combine(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => $"{call.ArgAt<string>(0)}\\{call.ArgAt<string>(1)}");

        file.Exists(Arg.Any<string>()).Returns(false);
        file.Exists(configPath).Returns(true);
        file.ReadAllText(configPath).Returns("""
            <configuration>
              <specFlow>
                <unitTestProvider name="nunit" />
              </specFlow>
            </configuration>
            """);

        directory.GetFiles(Arg.Any<string>(), "*.feature.cs", Arg.Any<SearchOption>()).Returns([]);

        string? written = null;
        file.When(f => f.WriteAllText(configPath, Arg.Any<string>()))
            .Do(call => written = call.ArgAt<string>(1));

        var changed = new SpecFlowRetargetService { File = file, Path = path, Directory = directory }
            .Retarget(Csproj);

        // The package alone is not enough: a config still naming the old provider fails at run time.
        Assert.Contains(@"name=""xunit""", written);
        Assert.Contains(configPath, changed);
    }

    // ---------------- bindings ----------------

    [Fact]
    public void Bindings_And_Hooks_Survive_While_Their_Asserts_Are_Converted()
    {
        var code = """
            using NUnit.Framework;
            using TechTalk.SpecFlow;

            [Binding]
            public class LoginSteps
            {
                [BeforeScenario]
                public void Before() { }

                [Then(@"the total is (.*)")]
                public void ThenTotal(int expected)
                {
                    Assert.AreEqual(expected, _total);
                }
            }
            """;

        var result = new XunitSyntaxRewriter().Visit(CSharpSyntaxTree.ParseText(code).GetRoot())!
            .NormalizeWhitespace()
            .ToFullString();

        // SpecFlow's own attributes are framework-agnostic and mean the same thing under any provider.
        Assert.Contains("[Binding]", result);
        Assert.Contains("[BeforeScenario]", result);
        Assert.Contains(@"[Then(@""the total is (.*)"")]", result);

        // Only the assertion belongs to the unit test framework.
        Assert.Contains("Assert.Equal(expected, _total)", result);
        Assert.DoesNotContain("AreEqual", result);
    }
}

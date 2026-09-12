using NSubstitute;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter.Tests;

public class TestPackagesRewriterTests
{
    private const string Path = @"C:\proj\Tests\Tests.csproj";

    /// <summary>Holds the text handed to <c>WriteAllText</c>, captured as the call happens.</summary>
    private sealed class Capture
    {
        public string Text { get; set; } = string.Empty;
    }

    private static (TestPackagesRewriter Sut, IFile File, Capture Written) Arrange(string csproj)
    {
        var sut = new TestPackagesRewriter();
        var file = Substitute.For<IFile>();
        sut.File = file;
        file.ReadAllText(Path).Returns(csproj);

        var capture = new Capture();
        file.When(f => f.WriteAllText(Path, Arg.Any<string>()))
            .Do(call => capture.Text = call.ArgAt<string>(1));

        return (sut, file, capture);
    }

    [Fact]
    public void Replaces_MsTest_Packages_With_The_Xunit_Set()
    {
        var (sut, file, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""MSTest.TestAdapter"" Version=""3.0.0"" />
    <PackageReference Include=""MSTest.TestFramework"" Version=""3.0.0"" />
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.0.0"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        var result = written.Text;
        Assert.DoesNotContain("MSTest.TestAdapter", result);
        Assert.DoesNotContain("MSTest.TestFramework", result);
        Assert.Contains(@"<PackageReference Include=""coverlet.collector"" Version=""6.0.4"" />", result);
        Assert.Contains(@"<PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.14.1"" />", result);
        Assert.Contains(@"<PackageReference Include=""xunit"" Version=""2.9.3"" />", result);
        Assert.Contains(@"<PackageReference Include=""xunit.runner.visualstudio"" Version=""3.1.4"" />", result);
        // The old 17.0.0 pin is gone, not merely duplicated.
        Assert.DoesNotContain(@"Version=""17.0.0""", result);
    }

    [Fact]
    public void Replaces_NUnit_And_Coverlet_Packages()
    {
        var (sut, file, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
    <PackageReference Include=""NUnit3TestAdapter"" Version=""4.4.2"" />
    <PackageReference Include=""NUnit.Analyzers"" Version=""3.6.1"" />
    <PackageReference Include=""coverlet.collector"" Version=""3.1.2"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        var result = written.Text;
        Assert.DoesNotContain("NUnit", result);
        Assert.DoesNotContain(@"Version=""3.1.2""", result);
        Assert.Contains(@"Include=""coverlet.collector"" Version=""6.0.4""", result);
    }

    [Fact]
    public void Keeps_Unrelated_Packages_And_Project_References()
    {
        var (sut, file, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Lib\Lib.csproj"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        var result = written.Text;
        Assert.Contains(@"Include=""Newtonsoft.Json"" Version=""13.0.3""", result);
        Assert.Contains(@"<ProjectReference Include=""..\Lib\Lib.csproj"" />", result);
    }

    [Fact]
    public void Writes_The_Item_Group_Where_The_First_Match_Lived()
    {
        var (sut, file, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Lib\Lib.csproj"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        var result = written.Text;
        // The replacement keeps the position of the group it replaced: packages before project references.
        Assert.True(result.IndexOf("xunit.runner.visualstudio", StringComparison.Ordinal)
                    < result.IndexOf("ProjectReference", StringComparison.Ordinal));
        // The emptied group is not left behind.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(result, "<ItemGroup>").Count);
    }

    [Fact]
    public void Preserves_The_Default_Namespace_Of_Legacy_Projects()
    {
        var (sut, file, written) = Arrange(@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        var result = written.Text;
        Assert.StartsWith(@"<?xml version=""1.0"" encoding=""utf-8""?>", result);
        // Inheriting the default namespace, the new items must not carry an xmlns="" reset.
        Assert.DoesNotContain(@"xmlns=""""", result);
        Assert.Contains(@"Include=""xunit""", result);
    }

    [Fact]
    public void Appends_The_Item_Group_When_The_Project_Has_No_Test_Packages()
    {
        var (sut, file, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        Assert.Contains(@"Include=""xunit""", written.Text);
    }

    [Fact]
    public void Running_Again_Over_Its_Own_Output_Changes_Nothing()
    {
        var (sut, _, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        // Converting a project twice must not keep rewriting it: feed the output back in.
        var (second, secondFile, _) = Arrange(written.Text);

        Assert.False(second.RewritePackageReferences(Path));
        secondFile.DidNotReceive().WriteAllText(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void Retargets_A_Project_Wide_Using_At_Xunit()
    {
        var (sut, _, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Net4x.MsTests"" Version=""1.5.0"" />
  </ItemGroup>
  <ItemGroup>
    <Using Include=""Microsoft.VisualStudio.TestTools.UnitTesting"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        // Left alone, the global using would reference a namespace the project no longer references.
        Assert.Contains(@"<Using Include=""Xunit"" />", written.Text);
        Assert.DoesNotContain("TestTools.UnitTesting", written.Text);
    }

    [Fact]
    public void Does_Not_Duplicate_An_Existing_Xunit_Using()
    {
        var (sut, _, written) = Arrange(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.13.3"" />
  </ItemGroup>
  <ItemGroup>
    <Using Include=""Xunit"" />
    <Using Include=""NUnit.Framework"" />
  </ItemGroup>
</Project>");

        Assert.True(sut.RewritePackageReferences(Path));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(written.Text, @"<Using Include=""Xunit"" />"));
    }
}

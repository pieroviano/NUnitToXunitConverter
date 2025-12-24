using ConversionClassLibrary;

namespace MsTestsToNunit.Tests;

public class FormatContentTests
{
    [Fact]
    public void FormatContent_Formats_Content_With_Indentation()
    {
        // Arrange
        var content = @"class Test{
                [Test]
                public void TestMethod()
                {
                 Console.WriteLine(""Test"");
                         Test();
                        while(true){
}
                }}";

        var expectedFormattedContent = Resources.FormatContentTests_FormatContent_Formats_Content_With_Indentation_;

        // Act
        var formattedContent = new MsTestToNUnitContent().FormatContent(content);

        // Assert
        Assert.Equal(expectedFormattedContent.Replace("\r\n", "\n"), formattedContent);
    }
}
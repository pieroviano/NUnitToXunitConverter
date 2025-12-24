#pragma warning disable warning xUnit2009

using ConversionClassLibrary;
#pragma warning disable xUnit2009

namespace MsTestsToNunit.Tests;

public class TransformMethodsTests
{
    [Fact]
    public void TransformMethods_Transforms_Methods_With_Expected_Exception()
    {
        // Arrange
        var originalContent = @"
                [TestMethod, ExpectedException(typeof(TaskCanceledException))]
                public void Cancel()
                {
                    TestUtils.RunAsync(async () =>
                    {
                        using (var cancelSource = new CancellationTokenSource())
                        {
                            cancelSource.CancelAfter(10);

                            await TaskEx.Delay(100, cancelSource.Token);
                        }
                    });
                }";

        // Act
        var transformedContent = new MsTestToNUnitContent().TransformMethods(originalContent);

        // Assert
        Assert.True(transformedContent.Contains("catch (TaskCanceledException e)"));
    }

    [Fact]
    public void TransformMethods_Does_Not_Transform_Methods_Without_Expected_Exception()
    {
        // Arrange
        var originalContent = @"
                [TestMethod]
                public void NormalMethod()
                {
                    // Method body without expected exception
                }";

        // Act
        var transformedContent = new MsTestToNUnitContent().TransformMethods(originalContent);

        // Assert
        Assert.False(transformedContent.Contains("catch (TaskCanceledException e)"));
    }
}
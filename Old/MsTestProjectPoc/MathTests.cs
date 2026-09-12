namespace TestProjectPoc
{
    [TestClass]
    public sealed class MathTests
    {
        [TestMethod]
        public void OnePlusOneTest()
        {
#pragma warning disable MSTEST0032
            Assert.AreEqual(2, 1 + 1);
#pragma warning restore MSTEST0032
        }
    }
}

using System.Reflection;
using NUnit.Framework;

namespace SkyKeyYuyan.Languages.Chinese.Tests
{
    public class AssemblySmokeTests
    {
        [Test]
        public void LibraryAssemblyLoads()
        {
            var assembly = Assembly.Load("SkyKeyYuyan.Languages.Chinese");

            Assert.That(assembly.GetName().Name, Is.EqualTo("SkyKeyYuyan.Languages.Chinese"));
        }
    }
}

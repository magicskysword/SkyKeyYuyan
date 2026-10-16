using System.Reflection;
using NUnit.Framework;

namespace SkyKeyYuyan.Languages.English.Tests
{
    public class AssemblySmokeTests
    {
        [Test]
        public void LibraryAssemblyLoads()
        {
            var assembly = Assembly.Load("SkyKeyYuyan.Languages.English");

            Assert.That(assembly.GetName().Name, Is.EqualTo("SkyKeyYuyan.Languages.English"));
        }
    }
}

using System.Reflection;
using NUnit.Framework;

namespace SkyKeyYuyan.Languages.Korean.Tests
{
    public class AssemblySmokeTests
    {
        [Test]
        public void LibraryAssemblyLoads()
        {
            var assembly = Assembly.Load("SkyKeyYuyan.Languages.Korean");

            Assert.That(assembly.GetName().Name, Is.EqualTo("SkyKeyYuyan.Languages.Korean"));
        }
    }
}

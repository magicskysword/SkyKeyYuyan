using System.Reflection;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class AssemblySmokeTests
    {
        [Test]
        public void LibraryAssemblyLoads()
        {
            var assembly = Assembly.Load("SkyKeyYuyan.Core");

            Assert.That(assembly.GetName().Name, Is.EqualTo("SkyKeyYuyan.Core"));
        }
    }
}

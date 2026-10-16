using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class KeyboardTests
    {
        [Test]
        public void ParsingPreservesSpaceOutputAndShiftAlternates()
        {
            var layout = KeyboardLayout.Parse(Encoding.UTF8.GetBytes("SKYKEYS\t1\ten\t2026.1\na\ta\ta\tA\t0\t0\tinput\nspace\t空格\t \t \t1\t0\tinput\n"), "en");
            Assert.That(layout["space"].Output, Is.EqualTo(" "));
            Assert.That(layout["a"].Alternate, Is.EqualTo("A"));
            Assert.That(layout.DataVersion, Is.EqualTo("2026.1"));
        }
        [TestCase("SKYKEYS\t2\ten\t1\na\ta\ta\tA\t0\t0\tinput\n")]
        [TestCase("SKYKEYS\t1\tko\t1\na\ta\ta\tA\t0\t0\tinput\n")]
        [TestCase("SKYKEYS\t1\ten\t1\na\ta\ta\tA\tleft\t0\tinput\n")]
        public void InvalidResourceIsRejected(string text)
        {
            Assert.Throws<InvalidDataException>(() => KeyboardLayout.Parse(Encoding.UTF8.GetBytes(text), "en"));
        }
        [Test]
        public void DuplicatePositionsCannotCreateAmbiguousNavigation()
        {
            Assert.Throws<ArgumentException>(() => new KeyboardLayout("en", "1", new[]
            {
                new KeyboardKey("a", "a", "a", "A", 0, 0), new KeyboardKey("b", "b", "b", "B", 0, 0)
            }));
        }
        [Test]
        public void UnevenRowsChooseNearestColumnAndWrap()
        {
            var layout = KeyboardLayout.BasicEnglish;
            Assert.That(layout.Navigate("key-1-9", TextNavigation.Down), Is.EqualTo("key-2-8"));
            Assert.That(layout.Navigate("key-1-0", TextNavigation.Left), Is.EqualTo("key-1-9"));
            Assert.That(layout.Navigate("key-0-0", TextNavigation.Up), Is.EqualTo("left"));
        }
        [Test]
        public void RuntimeFixesCustomLayoutAndSharesItAcrossSessions()
        {
            var layout = new KeyboardLayout("en", "custom.1", new[] { new KeyboardKey("x", "X", "x", "X", 0, 0) });
            var configured = new Dictionary<string, KeyboardLayout> { ["en"] = layout };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(new BasicEnglishModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, Keyboards = configured });
            configured.Clear();
            using var first = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            using var second = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            Assert.That(first.GetKeyboard(), Is.SameAs(layout));
            Assert.That(second.GetKeyboard(), Is.SameAs(layout));
            Assert.That(first.Languages[0].DisplayName, Is.EqualTo("English"));
        }
    }
}

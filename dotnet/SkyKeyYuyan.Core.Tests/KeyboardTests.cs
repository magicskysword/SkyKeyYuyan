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
        public void AlignedRowsKeepCharacterColumnsAndWrap()
        {
            var layout = KeyboardLayout.BasicEnglish;
            for (int column = 0; column < 7; column++)
                for (int row = 0; row < 3; row++)
                {
                    string current = "key-" + row + "-" + column, below = "key-" + (row + 1) + "-" + column;
                    Assert.That(layout.Navigate(current, TextNavigation.Down), Is.EqualTo(below));
                    Assert.That(layout.Navigate(below, TextNavigation.Up), Is.EqualTo(current));
                }
            Assert.That(layout.Navigate("key-1-9", TextNavigation.Down), Is.EqualTo("semicolon"));
            Assert.That(layout.Navigate("key-1-0", TextNavigation.Left), Is.EqualTo("delete"));
            Assert.That(layout.Navigate("key-0-0", TextNavigation.Up), Is.EqualTo("selectall"));
            Assert.That(layout.Navigate("space", TextNavigation.Up), Is.EqualTo("key-3-6"));
        }
        [Test]
        public void VerticalAnchorReturnsToTheSameKeyAcrossWideKeys()
        {
            var layout = KeyboardLayout.BasicEnglish;
            foreach (var key in layout.Keys)
            {
                double column = key.Column + key.ColumnSpan / 2d;
                var below = layout.Navigate(key.Id, TextNavigation.Down, column);
                Assert.That(layout.Navigate(below, TextNavigation.Up, column), Is.EqualTo(key.Id));
                var above = layout.Navigate(key.Id, TextNavigation.Up, column);
                Assert.That(layout.Navigate(above, TextNavigation.Down, column), Is.EqualTo(key.Id));
            }
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
        [Test]
        public void WideKeyNavigationUsesCentersAndEveryKeyRemainsReachable()
        {
            var layout = KeyboardLayout.Parse(Encoding.UTF8.GetBytes("SKYKEYS\t2\ten\t2\nleft\tL\tl\tL\t0\t0\tinput\t2\nmid\tM\tm\tM\t0\t2\tinput\t2\nright\tR\tr\tR\t0\t4\tinput\t2\nspace\t空格\t \t \t1\t0\tinput\t6\n"), "en");
            Assert.That(layout["space"].ColumnSpan, Is.EqualTo(6));
            Assert.That(layout.ColumnCount, Is.EqualTo(6)); Assert.That(layout.RowCount, Is.EqualTo(2));
            Assert.That(layout.Navigate("space", TextNavigation.Up), Is.EqualTo("mid"));
            Assert.That(layout.Navigate("right", TextNavigation.Right), Is.EqualTo("left"));
            var visited = new HashSet<string>(); var pending = new Queue<string>(); pending.Enqueue("space");
            while (pending.Count > 0)
            {
                string key = pending.Dequeue(); if (!visited.Add(key)) continue;
                foreach (var direction in new[] { TextNavigation.Left, TextNavigation.Right, TextNavigation.Up, TextNavigation.Down }) pending.Enqueue(layout.Navigate(key, direction));
            }
            Assert.That(visited.Count, Is.EqualTo(layout.Keys.Count));
        }
        [Test]
        public void OverlappingSpansCannotCreateAmbiguousHitRegions()
        {
            Assert.Throws<ArgumentException>(() => new KeyboardLayout("en", "2", new[]
            {
                new KeyboardKey("space", "空格", " ", " ", 0, 0, "input", 6),
                new KeyboardKey("right", "R", "r", "R", 0, 5)
            }));
        }
    }
}

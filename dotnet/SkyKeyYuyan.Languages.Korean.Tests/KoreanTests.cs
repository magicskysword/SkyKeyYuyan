using System;
using System.Text;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Korean.Tests
{
    public class KoreanTests
    {
        [TestCase("ㄱㅏㄴㅏ", "가나")]
        [TestCase("rkskek", "가나다")]
        [TestCase("RkEkQkTkWk", "까따빠싸짜")]
        [TestCase("rhk", "과")]
        [TestCase("rnj", "궈")]
        [TestCase("rnl", "귀")]
        [TestCase("rml", "긔")]
        [TestCase("rkqt", "값")]
        [TestCase("rkqtk", "갑사")]
        [TestCase("rkfrk", "갈가")]
        [TestCase("dkssudgktpdy", "안녕하세요")]
        public void TwoSetInputComposesModernSyllables(string raw, string expected)
        {
            Assert.That(HangulComposer.Compose(raw), Is.EqualTo(expected));
        }

        [Test]
        public void AllModernSyllablesAgreeWithUnicodeCanonicalComposition()
        {
            for (int value = 0xac00; value <= 0xd7a3; value++)
            {
                string expected = ((char)value).ToString();
                string jamo = expected.Normalize(NormalizationForm.FormD);
                Assert.That(HangulComposer.Compose(jamo), Is.EqualTo(expected), "韩文音节 " + value);
            }
        }

        [Test]
        public void UndoRestoresFinalBeforeVowelRedistribution()
        {
            using var engine = new KoreanModule().CreateEngine();
            engine.Process(CompositionCommand.Insert("ㄱㅏㄴㅏ"));
            Assert.That(engine.Snapshot.DisplayText, Is.EqualTo("가나"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("간"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("가"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("ㄱ"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.IsEmpty, Is.True);
        }

        [Test]
        public void CheckpointRetainsCompoundVowelAndCodaHistory()
        {
            using var module = new KoreanModule();
            using var original = module.CreateEngine(); original.Process(CompositionCommand.Insert("rhkrt"));
            var checkpoint = original.CaptureCheckpoint();
            using var rebuilt = module.CreateEngine();
            Assert.That(rebuilt.RestoreCheckpoint(checkpoint).Succeeded, Is.True);
            Assert.That(rebuilt.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("곽"));
            Assert.That(rebuilt.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("과"));
        }

        [Test]
        public void MixedWordAndDelimiterInputIsCommittedInOrder()
        {
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(new KoreanModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            Assert.That(session.Process(InputOperation.Insert("rkskek rksk")).Status, Is.EqualTo(OperationStatus.Applied));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("가나다 "));
            Assert.That(session.Snapshot.Composition.DisplayText, Is.EqualTo("가나"));
            session.Process(InputOperation.Finish());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("가나다 가나"));
        }

        [Test]
        public void RejectedBoundaryPreservesUnprocessedPasteText()
        {
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(new KoreanModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var target = new MemoryTextTarget(validator: _ => false);
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            session.Process(InputOperation.Insert("rksk rest"));
            Assert.That(session.Snapshot.Composition.RawText, Is.EqualTo("rksk rest"));
            Assert.That(session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
        }
    }
}

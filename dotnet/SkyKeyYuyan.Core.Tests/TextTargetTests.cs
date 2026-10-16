using System;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class TextTargetTests
    {
        private readonly Guid _session = Guid.NewGuid();
        private OperationId Op(long sequence = 1) => new OperationId(_session, sequence);

        [Test]
        public void LimitedInsertionPreservesUnselectedSuffix()
        {
            var target = new MemoryTextTarget("ABCD", 2, 2, 5);
            var result = target.ApplyEdit(new TextEditRequest(Op(), 0, 2, 0, "XYZ"));
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(TextEditStatus.Truncated));
                Assert.That(result.Snapshot.Text, Is.EqualTo("ABXCD"));
                Assert.That(result.AcceptedText, Is.EqualTo("X"));
            });
        }

        [Test]
        public void WholeDraftReplacementUsesWholeTextPrefix()
        {
            var target = new MemoryTextTarget("ABCD", maxLength: 5);
            var result = target.ApplyEdit(new TextEditRequest(Op(), 0, 0, 4, "ABXYZCD"));
            Assert.That(result.Snapshot.Text, Is.EqualTo("ABXYZ"));
        }

        [Test]
        public void ZeroCapacityStillDeletesSelectedRange()
        {
            var target = new MemoryTextTarget("ABCD", 1, 2, 2);
            var result = target.ApplyEdit(new TextEditRequest(Op(), 0, 1, 1, "XYZ"));
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(TextEditStatus.Truncated));
                Assert.That(result.Snapshot.Text, Is.EqualTo("ACD"));
            });
        }

        [Test]
        public void ReplayReturnsOriginalResultAndDoesNotInsertAgain()
        {
            var target = new MemoryTextTarget();
            var request = new TextEditRequest(Op(), 0, 0, 0, "你好");
            var first = target.ApplyEdit(request);
            target.ApplyEdit(new TextEditRequest(Op(2), first.Snapshot.Version, 2, 0, "世界"));
            Assert.That(target.ApplyEdit(request), Is.SameAs(first));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("你好世界"));
        }

        [Test]
        public void InsertionInsideSurrogatePairDoesNotDeleteExistingCharacter()
        {
            var target = new MemoryTextTarget("A😀B");
            var result = target.ApplyEdit(new TextEditRequest(Op(), 0, 2, 0, "X"));
            Assert.That(result.Snapshot.Text, Is.EqualTo("AX😀B"));
        }

        [Test]
        public void RejectedValidationPreservesSnapshotAndCanBeRetried()
        {
            var target = new MemoryTextTarget(validator: text => text != "bad");
            Assert.That(target.ApplyEdit(new TextEditRequest(Op(), 0, 0, 0, "bad")).Status, Is.EqualTo(TextEditStatus.Rejected));
            Assert.That(target.ReadSnapshot().Version, Is.Zero);
            Assert.That(target.ApplyEdit(new TextEditRequest(Op(2), 0, 0, 0, "good")).Snapshot.Text, Is.EqualTo("good"));
        }

        [Test]
        public void ExternalEditInvalidatesExpectedVersionAndOnlyExternalChangesNotify()
        {
            var target = new MemoryTextTarget();
            int changes = 0;
            target.Changed += _ => changes++;
            target.ApplyEdit(new TextEditRequest(Op(), 0, 0, 0, "A"));
            target.SetExternalText("B");
            Assert.That(target.ApplyEdit(new TextEditRequest(Op(2), 1, 1, 0, "C")).Status, Is.EqualTo(TextEditStatus.VersionConflict));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void SurrogatePairsRemainWholeAcrossNavigationDeletionAndTruncation()
        {
            var target = new MemoryTextTarget("A😀B", 3, 3);
            var move = target.Navigate(new TextNavigationRequest(Op(), 0, TextNavigation.Left));
            Assert.That(move.Snapshot.Caret, Is.EqualTo(1));
            var deletion = target.ApplyEdit(new TextEditRequest(Op(2), move.Snapshot.Version, 2, 1, ""));
            Assert.That(deletion.Snapshot.Text, Is.EqualTo("AB"));
            var limited = new MemoryTextTarget(maxLength: 1);
            Assert.That(limited.ApplyEdit(new TextEditRequest(Op(3), 0, 0, 0, "😀")).AcceptedText, Is.Empty);
        }

        [Test]
        public void MultilineNavigationKeepsAnchorWhenExtendingSelection()
        {
            var target = new MemoryTextTarget("abc\ndefg\nh", 2, 2);
            var down = target.Navigate(new TextNavigationRequest(Op(), 0, TextNavigation.Down, true));
            Assert.Multiple(() =>
            {
                Assert.That(down.Snapshot.Anchor, Is.EqualTo(2));
                Assert.That(down.Snapshot.Caret, Is.EqualTo(6));
            });
            var second = target.Navigate(new TextNavigationRequest(Op(2), down.Snapshot.Version, TextNavigation.Down, true));
            Assert.That(second.Snapshot.Caret, Is.EqualTo(10));
        }
    }
}

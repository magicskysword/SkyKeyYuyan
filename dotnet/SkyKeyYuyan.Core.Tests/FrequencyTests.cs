using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class FrequencyTests
    {
        private static ChoiceRecord Choice(string id, long operation = 1, string user = "user", string language = "zh", Guid? session = null) => new ChoiceRecord(id, new OperationId(session ?? Guid.Empty, operation), user, language, new[] { new WordUsage("word") });

        [Test]
        public async Task SaveWaitsForBackgroundLoadAndMergesExistingCounts()
        {
            var store = new Store { LoadGate = Gate() };
            store.Seed(new FrequencySnapshot("user", "zh", 7, new Dictionary<string, long> { ["word"] = 100 }));
            var service = new FrequencyService(store);
            var firstLoad = service.LoadAsync("user", "zh");
            Assert.That(service.LoadAsync("user", "zh"), Is.SameAs(firstLoad));
            service.Record(Choice("choice"));
            Assert.That(service.Capture("user", "zh"), Is.Empty);
            Assert.That(service.Save("user", "zh").Pending, Is.True);
            Assert.That(store.Writes, Is.Empty);
            store.LoadGate.SetResult(true);
            await firstLoad;
            await service.SaveAsync("user", "zh", CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(store.Loads, Is.EqualTo(1));
                Assert.That(service.Capture("user", "zh")["word"], Is.EqualTo(101));
                Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(101));
            });
        }

        [Test]
        public async Task NewChoicesDuringSaveRemainDirtyAfterOlderRevisionCompletes()
        {
            var store = new Store { SaveGate = Gate() };
            var service = new FrequencyService(store); service.Load("user", "zh");
            service.Record(Choice("first"));
            var saving = service.SaveAsync("user", "zh", CancellationToken.None);
            Assert.That(store.Writes.Count, Is.EqualTo(1));
            service.Record(Choice("second", 2));
            store.SaveGate.SetResult(true); await saving;
            Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(1));
            await service.SaveAsync("user", "zh", CancellationToken.None);
            Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(2));
            Assert.That(store.Writes.Select(snapshot => snapshot.Revision), Is.Ordered.Ascending);
        }

        [Test]
        public void SaveFailureKeepsTheSameRevisionAvailableForRetry()
        {
            var store = new Store { SaveFailures = 1 };
            var service = new FrequencyService(store); service.Load("user", "zh"); service.Record(Choice("first"));
            Assert.That(service.Save("user", "zh").Result.Succeeded, Is.False);
            Assert.That(service.Save("user", "zh").Result.Succeeded, Is.True);
            Assert.That(store.Writes.Select(snapshot => snapshot.Revision).Distinct().Count(), Is.EqualTo(1));
            Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(1));
        }

        [Test]
        public void FailedLoadsRetryOnceThenAllowSavingFreshCounts()
        {
            var store = new Store { LoadFailures = 2 };
            store.Seed(new FrequencySnapshot("user", "zh", 4, new Dictionary<string, long> { ["word"] = 100 }));
            var service = new FrequencyService(store);
            var diagnostics = new List<SkyDiagnostic>(); service.Diagnostic += diagnostics.Add;
            Assert.That(service.Load("user", "zh").Result.Succeeded, Is.False);
            Assert.That(store.Loads, Is.EqualTo(2));
            service.Record(Choice("new"));
            Assert.That(service.Save("user", "zh").Result.Succeeded, Is.True);
            Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(1));
            Assert.That(diagnostics.Select(item => item.Stage), Does.Contain("frequency-load").And.Contain("frequency-save"));
        }

        [Test]
        public void RepeatedChoiceAndConcurrentSessionsAccumulateExactlyOnce()
        {
            var service = new FrequencyService(new MemoryFrequencyStore()); service.Load("user", "zh");
            var record = Choice("same");
            Assert.That(service.Record(record), Is.True);
            Assert.That(service.Record(record), Is.False);
            Parallel.For(0, 500, i => service.Record(Choice("choice" + i, i + 2, session: Guid.NewGuid())));
            Assert.That(service.Capture("user", "zh")["word"], Is.EqualTo(501));
            Assert.That(service.Capture("other", "zh"), Is.Empty);
            Assert.That(service.Capture("user", "en"), Is.Empty);
        }

        [Test]
        public void UnsupportedStoredFormatIsReportedAndDoesNotLeakCounts()
        {
            var store = new Store();
            store.Seed(new FrequencySnapshot("user", "zh", 1, new Dictionary<string, long> { ["word"] = 5 }, formatVersion: 2));
            var service = new FrequencyService(store);
            Assert.That(service.Load("user", "zh").Result.Succeeded, Is.False);
            Assert.That(store.Loads, Is.EqualTo(2));
            Assert.That(service.Capture("user", "zh"), Is.Empty);
        }

        [Test]
        public async Task WaitingSaveCanBeCancelledWithoutLosingNewCounts()
        {
            var store = new Store { SaveGate = Gate() };
            var service = new FrequencyService(store); service.Load("user", "zh"); service.Record(Choice("first"));
            var first = service.SaveAsync("user", "zh", CancellationToken.None);
            service.Record(Choice("second", 2));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var cancelled = await service.SaveAsync("user", "zh", cancellation.Token);
            Assert.That(cancelled.Result.Cancelled, Is.True);
            store.SaveGate.SetResult(true); await first;
            await service.SaveAsync("user", "zh", CancellationToken.None);
            Assert.That(store.Load("user", "zh").Snapshot!.Counts["word"], Is.EqualTo(2));
        }

        [Test]
        public void DraftCancellationDiscardsRecordsAndSuccessfulSaveSharesThemAcrossRuntimes()
        {
            string user = Guid.NewGuid().ToString("N");
            using var first = Probe(); using var second = Probe();
            using var direct = first.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct, UserId = user }, new MemoryTextTarget("A", maxLength: 1)).Session!;
            ChooseProbe(direct);
            Assert.That(FrequencyService.Shared.Capture(user, "probe")["probe.word"], Is.EqualTo(1));
            using var cancelled = second.OpenSession(new SessionOptions { UserId = user }, new MemoryTextTarget()).Session!;
            ChooseProbe(cancelled); cancelled.Process(InputOperation.Cancel());
            Assert.That(FrequencyService.Shared.Capture(user, "probe")["probe.word"], Is.EqualTo(1));
            using var saved = second.OpenSession(new SessionOptions { UserId = user }, new MemoryTextTarget()).Session!;
            ChooseProbe(saved); saved.Process(InputOperation.Finish());
            Assert.That(FrequencyService.Shared.Capture(user, "probe")["probe.word"], Is.EqualTo(2));
        }

        private static SkyRuntime Probe()
        {
            var runtime = new SkyRuntime(); runtime.RegisterLanguage(new RuntimeTests.ProbeModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            return runtime;
        }
        private static void ChooseProbe(ISkySession session)
        {
            session.Process(InputOperation.Insert("a")); RuntimeTests.WaitFor(() => session.Snapshot.Candidates != null);
            var page = session.Snapshot.Candidates!;
            Assert.That(session.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id)).Status, Is.EqualTo(OperationStatus.Applied));
        }
        private static TaskCompletionSource<bool> Gate() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private sealed class Store : IFrequencyStore
        {
            private readonly MemoryFrequencyStore _inner = new MemoryFrequencyStore();
            internal readonly ConcurrentQueue<FrequencySnapshot> Writes = new ConcurrentQueue<FrequencySnapshot>();
            internal int Loads, LoadFailures, SaveFailures;
            internal TaskCompletionSource<bool>? LoadGate, SaveGate;
            internal void Seed(FrequencySnapshot snapshot) => _inner.Save(snapshot);
            public FrequencyStoreResult Load(string userId, string languageId)
            {
                Interlocked.Increment(ref Loads);
                if (Interlocked.Decrement(ref LoadFailures) >= 0) throw new InvalidOperationException("load fault");
                return _inner.Load(userId, languageId);
            }
            public async Task<FrequencyStoreResult> LoadAsync(string userId, string languageId, CancellationToken cancellationToken)
            {
                if (LoadGate != null) await LoadGate.Task.ConfigureAwait(false);
                return Load(userId, languageId);
            }
            public FrequencyStoreResult Save(FrequencySnapshot snapshot)
            {
                Writes.Enqueue(snapshot);
                if (Interlocked.Decrement(ref SaveFailures) >= 0) throw new InvalidOperationException("save fault");
                return _inner.Save(snapshot);
            }
            public async Task<FrequencyStoreResult> SaveAsync(FrequencySnapshot snapshot, CancellationToken cancellationToken)
            {
                Writes.Enqueue(snapshot);
                if (SaveGate != null) await SaveGate.Task.ConfigureAwait(false);
                if (Interlocked.Decrement(ref SaveFailures) >= 0) throw new InvalidOperationException("save fault");
                return _inner.Save(snapshot);
            }
        }
    }
}

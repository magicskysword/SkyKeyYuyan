using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class RuntimeTests
    {
        private static SkyRuntime English(bool replaceDefault = false)
        {
            var runtime = new SkyRuntime();
            runtime.RegisterLanguage(new BasicEnglishModule());
            Assert.That(runtime.Initialize(new RuntimeOptions { ReplaceDefault = replaceDefault }).Succeeded, Is.True);
            return runtime;
        }

        [Test]
        public void DirectAndDraftModesApplyDifferentLengthRules()
        {
            using var runtime = English();
            var directTarget = new MemoryTextTarget("ABCD", 2, 2, 5);
            using var direct = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, directTarget).Session!;
            direct.Process(InputOperation.Insert("XYZ"));
            Assert.That(directTarget.ReadSnapshot().Text, Is.EqualTo("ABXCD"));
            var draftTarget = new MemoryTextTarget("ABCD", 2, 2, 5);
            using var draft = runtime.OpenSession(new SessionOptions(), draftTarget).Session!;
            draft.Process(InputOperation.Insert("XYZ"));
            Assert.That(draft.Snapshot.Text.Text, Is.EqualTo("ABXYZCD"));
            Assert.That(draftTarget.ReadSnapshot().Text, Is.EqualTo("ABCD"));
            draft.Process(InputOperation.Finish());
            Assert.That(draftTarget.ReadSnapshot().Text, Is.EqualTo("ABXYZ"));
        }

        [Test]
        public void CustomDraftRetainsFullTextAndUsesItsOwnNavigation()
        {
            using var runtime = English();
            var target = new MemoryTextTarget("ABCD", 2, 2, 5);
            MemoryTextTarget? draft = null;
            using var session = runtime.OpenSession(new SessionOptions
            {
                DraftTargetFactory = original => draft = new MemoryTextTarget(original.Text, original.Anchor, original.Caret)
            }, target).Session!;
            session.Process(InputOperation.Insert("XYZ"));
            Assert.That(draft!.ReadSnapshot().Text, Is.EqualTo("ABXYZCD"));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ABCD"));
            draft.SetExternalText("ABXYZCD", 0, 0);
            session.Process(InputOperation.Insert("Q"));
            Assert.That(session.Snapshot.Text.Text, Is.EqualTo("QABXYZCD"));
            session.Process(InputOperation.Finish());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("QABXY"));
        }

        [Test]
        public void DraftCannotAliasHostOrAnotherSessionsTarget()
        {
            using var first = English(); using var second = English();
            var target = new MemoryTextTarget("A");
            Assert.That(first.OpenSession(new SessionOptions { DraftTargetFactory = _ => target }, target).Succeeded, Is.False);
            var sharedDraft = new MemoryTextTarget("A");
            var options = new SessionOptions { DraftTargetFactory = _ => sharedDraft };
            using var session = first.OpenSession(options, target).Session!;
            var other = new MemoryTextTarget("A");
            Assert.That(second.OpenSession(options, other).Succeeded, Is.False);
            session.Dispose();
            using var next = second.OpenSession(options, other).Session;
            Assert.That(next, Is.Not.Null);
        }

        [Test]
        public void FailedDraftFactoryReleasesBothTargetLeases()
        {
            using var runtime = English();
            var target = new MemoryTextTarget("A");
            var mismatched = new MemoryTextTarget("different");
            Assert.That(runtime.OpenSession(new SessionOptions { DraftTargetFactory = _ => mismatched }, target).Succeeded, Is.False);
            using var first = runtime.OpenSession(new SessionOptions(), target).Session;
            using var second = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, mismatched).Session;
            Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.Null);
        }

        [Test]
        public void DraftInvalidationEndsOnceAndReleasesTheHost()
        {
            using var runtime = English();
            var target = new MemoryTextTarget("A"); var draft = new MemoryTextTarget("A");
            using var session = runtime.OpenSession(new SessionOptions { DraftTargetFactory = _ => draft }, target).Session!;
            int completed = 0;
            session.Completed += result => { completed++; Assert.That(result.Reason, Is.EqualTo(SessionEndReason.TargetInvalidated)); };
            draft.Invalidate(); draft.Invalidate();
            Assert.That(completed, Is.EqualTo(1));
            using var next = runtime.OpenSession(new SessionOptions(), target).Session;
            Assert.That(next, Is.Not.Null);
        }

        [TestCase(EditingMode.Direct, "originalnew")]
        [TestCase(EditingMode.Draft, "original")]
        public void CancelRespectsEditingModeAndCompletesOnce(EditingMode mode, string expected)
        {
            using var runtime = English();
            var target = new MemoryTextTarget("original");
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = mode }, target).Session!;
            int completions = 0;
            session.Completed += result => { completions++; Assert.That(result.Reason, Is.EqualTo(SessionEndReason.Cancelled)); };
            session.Process(InputOperation.Insert("new"));
            session.Process(InputOperation.Cancel()); session.Dispose(); runtime.Dispose();
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo(expected));
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void TwoRuntimesArbitrateByHostIdentityAcrossDifferentAdapters()
        {
            using var first = English(); using var second = English();
            var identity = new object();
            var target = new MemoryTextTarget(identity: identity);
            var alias = new MemoryTextTarget(identity: identity);
            using var session = first.OpenSession(new SessionOptions(), target).Session!;
            Assert.That(second.OpenSession(new SessionOptions(), alias).Succeeded, Is.False);
            session.Dispose();
            using var next = second.OpenSession(new SessionOptions(), alias).Session;
            Assert.That(next, Is.Not.Null);
        }

        [Test]
        public void ExternalChangeClosesDraftAndPreservesHostText()
        {
            using var runtime = English();
            var target = new MemoryTextTarget("A");
            using var session = runtime.OpenSession(new SessionOptions(), target).Session!;
            SessionResult? result = null; session.Completed += value => result = value;
            session.Process(InputOperation.Insert("draft"));
            target.SetExternalText("host");
            session.Process(InputOperation.Finish());
            Assert.That(result?.Reason, Is.EqualTo(SessionEndReason.ExternalChange));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("host"));
        }

        [Test]
        public void NotificationExceptionsDoNotReenterOrRepeatAnEdit()
        {
            var reports = new List<SkyDiagnostic>();
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(new BasicEnglishModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, DiagnosticSink = reports.Add });
            var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            int depth = 0, maxDepth = 0;
            session.StateChanged += snapshot =>
            {
                depth++; maxDepth = Math.Max(depth, maxDepth);
                if (snapshot.Text.Text == "A") session.Process(InputOperation.Insert("B"));
                depth--;
                throw new InvalidOperationException("listener");
            };
            session.Process(InputOperation.Insert("A"));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB"));
            Assert.That(maxDepth, Is.EqualTo(1));
            Assert.That(reports.Any(report => report.Stage == "notification"), Is.True);
        }

        [Test]
        public void RejectedDraftFillRemainsEditable()
        {
            using var runtime = English();
            var target = new MemoryTextTarget(validator: text => text != "bad");
            using var session = runtime.OpenSession(new SessionOptions(), target).Session!;
            session.Process(InputOperation.Insert("bad"));
            Assert.That(session.Process(InputOperation.Finish()).Status, Is.EqualTo(OperationStatus.Rejected));
            Assert.That(session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
            session.Process(InputOperation.Clear()); session.Process(InputOperation.Insert("good")); session.Process(InputOperation.Finish());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("good"));
        }

        [Test]
        public void RejectedDirectInputCanBeCorrectedInComposition()
        {
            using var runtime = English();
            var target = new MemoryTextTarget(validator: text => text != "bad");
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            session.Process(InputOperation.Insert("bad"));
            Assert.That(session.Snapshot.Composition.DisplayText, Is.EqualTo("bad"));
            session.Process(InputOperation.Backspace()); session.Process(InputOperation.Backspace()); session.Process(InputOperation.Backspace()); session.Process(InputOperation.Insert("good"));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("good"));
        }

        [Test]
        public void DefaultReplacementKeepsExistingSessionAndReleasesOldInstanceAfterward()
        {
            var module = new ProbeModule();
            using var old = new SkyRuntime(); old.RegisterLanguage(module); old.Initialize(new RuntimeOptions());
            var target = new MemoryTextTarget();
            using var session = old.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            using var replacement = English(true);
            Assert.That(SkyRuntime.Default, Is.SameAs(replacement));
            session.Process(InputOperation.Insert("a"));
            Assert.That(session.Snapshot.Composition.RawText, Is.EqualTo("a"));
            Assert.That(module.DisposeCount, Is.Zero);
            session.Dispose();
            WaitFor(() => module.DisposeCount == 1);
        }

        [Test]
        public void FailedInitializationReleasesAcquiredResourcesAndCanRetry()
        {
            var module = new ProbeModule { FailInitialize = true };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module);
            Assert.That(runtime.Initialize(new RuntimeOptions { ReplaceDefault = false }).Succeeded, Is.False);
            Assert.That(module.DisposeCount, Is.EqualTo(1));
            module.FailInitialize = false;
            Assert.That(runtime.Initialize(new RuntimeOptions { ReplaceDefault = false }).Succeeded, Is.True);
            runtime.Dispose();
            Assert.That(module.DisposeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task CancelledInitializationDoesNotReplaceDefault()
        {
            using var current = English(true);
            using var next = new SkyRuntime(); next.RegisterLanguage(new BasicEnglishModule());
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var result = await next.InitializeAsync(new RuntimeOptions(), cancellation.Token);
            Assert.That(result.Result.Cancelled, Is.True);
            Assert.That(SkyRuntime.Default, Is.SameAs(current));
        }

        [Test]
        public void NewLanguageIsOnlyAvailableToNewSessions()
        {
            using var runtime = English();
            using var original = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            var module = new ProbeModule(); runtime.RegisterLanguage(module);
            Assert.That(runtime.InitializeLanguage(module.EngineId).Succeeded, Is.True);
            Assert.That(original.Process(InputOperation.SwitchLanguage("probe")).Status, Is.EqualTo(OperationStatus.Rejected));
            using var next = runtime.OpenSession(new SessionOptions { InitialLanguage = "probe" }, new MemoryTextTarget()).Session!;
            Assert.That(next.Snapshot.LanguageId, Is.EqualTo("probe"));
        }

        [Test]
        public void RuntimeDisposalCancelsOpenSessionsWithSpecificReason()
        {
            using var runtime = English();
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            SessionResult? result = null; session.Completed += value => result = value;
            runtime.Dispose();
            Assert.That(result?.Reason, Is.EqualTo(SessionEndReason.RuntimeDisposed));
        }

        [Test]
        public void QueryLifetimeOutlivesCancelledSessionAndRetiredRuntime()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var module = new ProbeModule { QueryGate = gate };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("a"));
            WaitFor(() => module.QueriesStarted.Count == 1);
            runtime.Dispose();
            Assert.That(module.DisposeCount, Is.Zero);
            Assert.That(module.EnginesDisposed, Is.Zero);
            gate.SetResult(true);
            WaitFor(() => module.DisposeCount == 1 && module.EnginesDisposed == 1);
            Assert.That(session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Ended));
        }

        [Test]
        public void FinishWaitsForCandidatesAndResumesOnlyAfterUnpausing()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var module = new ProbeModule { QueryGate = gate };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.RegisterLanguage(new BasicEnglishModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions { InitialLanguage = "probe", EditingMode = EditingMode.Direct }, target).Session!;
            using var other = runtime.OpenSession(new SessionOptions { InitialLanguage = "en", EditingMode = EditingMode.Direct }, new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("abc"));
            Assert.That(session.Process(InputOperation.Finish()).Status, Is.EqualTo(OperationStatus.Pending));
            Assert.That(session.Process(InputOperation.Insert("x")).Status, Is.EqualTo(OperationStatus.Rejected));
            Assert.That(other.Process(InputOperation.Insert("other")).Status, Is.EqualTo(OperationStatus.Applied));
            session.Process(InputOperation.Pause()); gate.SetResult(true);
            WaitFor(() => !session.Snapshot.Querying);
            Assert.That(target.ReadSnapshot().Text, Is.Empty);
            session.Process(InputOperation.Resume());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ABC"));
            Assert.That(session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Ended));
        }

        [Test]
        public void LateQueryCannotOverwriteNewerCompositionResult()
        {
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var module = new ProbeModule
            {
                QueryHandler = async query =>
                {
                    await (query.Composition.RawText == "a" ? first.Task : second.Task).ConfigureAwait(false);
                    return new CandidatePage(query, new[] { new Candidate("value", query.Composition.RawText.ToUpperInvariant(), query.Composition.RawText.Length) });
                }
            };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("a")); WaitFor(() => module.QueriesStarted.Count == 1);
            session.Process(InputOperation.Insert("b")); WaitFor(() => module.QueriesStarted.Count == 2);
            second.SetResult(true); WaitFor(() => session.Snapshot.Candidates != null);
            long current = session.Snapshot.Candidates!.Query.Id;
            first.SetResult(true); WaitFor(() => module.QueriesFinished == 2);
            Assert.That(session.Snapshot.Candidates!.Query.Id, Is.EqualTo(current));
            Assert.That(session.Snapshot.Candidates.Candidates[0].Text, Is.EqualTo("AB"));
        }

        [Test]
        public void RepeatedQueryFailureFallsBackAndAllowsLiteralEditing()
        {
            var module = new ProbeModule { QueryFailures = 2 };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions(), target).Session!;
            session.Process(InputOperation.Insert("a")); WaitFor(() => session.Snapshot.Literal);
            Assert.That(module.QueriesStarted.Count, Is.EqualTo(2));
            session.Process(InputOperation.Insert("b")); session.Process(InputOperation.Finish());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ab"));
        }

        [Test]
        public void ProcessingFailureRebuildsCheckpointAndDoesNotRepeatInput()
        {
            var module = new ProbeModule { ProcessFailures = 1 };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("a"));
            Assert.That(session.Snapshot.Composition.RawText, Is.EqualTo("a"));
            Assert.That(session.Snapshot.Literal, Is.False);
            Assert.That(module.EnginesDisposed, Is.EqualTo(1));
        }

        [Test]
        public void LiteralRecoveryPreservesEditsMadeAfterFailure()
        {
            var module = new ProbeModule { QueryFailures = 2 };
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(module); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("a")); WaitFor(() => session.Snapshot.Literal);
            session.Process(InputOperation.Insert("b")); session.Process(InputOperation.RetryConversion());
            WaitFor(() => session.Snapshot.Candidates != null);
            Assert.That(session.Snapshot.Composition.RawText, Is.EqualTo("ab"));
            Assert.That(session.Snapshot.Candidates!.Candidates[0].Text, Is.EqualTo("AB"));
        }

        [Test]
        public void UnknownWriteRequiresExplicitResolutionAndIsNotAutomaticallyRepeated()
        {
            using var runtime = English();
            var target = new ThrowAfterWriteTarget();
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            var receipt = session.Process(InputOperation.Insert("A"));
            Assert.That(receipt.Status, Is.EqualTo(OperationStatus.NeedsResolution));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A"));
            Assert.That(session.Process(InputOperation.Insert("B")).Status, Is.EqualTo(OperationStatus.Rejected));
            session.Process(InputOperation.ResolveWrite(false));
            session.Process(InputOperation.Insert("B"));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB"));
            Assert.That(target.Writes, Is.EqualTo(2));
        }

        internal static void WaitFor(Func<bool> condition)
        {
            Assert.That(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), Is.True, "后台工作未在期限内完成");
        }

        private sealed class ThrowAfterWriteTarget : ITextTarget
        {
            private readonly MemoryTextTarget _target = new MemoryTextTarget();
            internal int Writes;
            public object Identity => _target;
            public event Action<TextTargetChange> Changed { add => _target.Changed += value; remove => _target.Changed -= value; }
            public TextSnapshot ReadSnapshot() => _target.ReadSnapshot();
            public TextNavigationResult Navigate(TextNavigationRequest request) => _target.Navigate(request);
            public TextEditResult ApplyEdit(TextEditRequest request)
            {
                var result = _target.ApplyEdit(request);
                if (++Writes == 1) throw new InvalidOperationException("after write");
                return result;
            }
        }

        internal sealed class ProbeModule : ILanguageModule
        {
            private sealed class Engine : HistoryLanguageEngine
            {
                private readonly ProbeModule _module;
                internal Engine(ProbeModule module) : base("probe.engine") { _module = module; }
                protected override bool UsesCandidates => true;
                public override EngineStep Process(CompositionCommand command)
                {
                    if (command.Kind == "input" && _module.ProcessFailures-- > 0) throw new InvalidOperationException("process fault");
                    return base.Process(command);
                }
                public override async Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken)
                {
                    _module.QueriesStarted.Add(query.Id);
                    try
                    {
                        if (_module.QueryFailures-- > 0) throw new InvalidOperationException("query fault");
                        if (_module.QueryHandler != null) return await _module.QueryHandler(query).ConfigureAwait(false);
                        if (_module.QueryGate != null) await _module.QueryGate.Task.ConfigureAwait(false);
                        return new CandidatePage(query, new[] { new Candidate("first", query.Composition.RawText.ToUpperInvariant(), query.Composition.RawText.Length, new[] { new WordUsage("probe.word") }) });
                    }
                    finally { Interlocked.Increment(ref _module.QueriesFinished); }
                }
                public override void Dispose() { Interlocked.Increment(ref _module.EnginesDisposed); base.Dispose(); }
            }
            public string LanguageId => "probe";
            public string EngineId => "probe.engine";
            public string DisplayName => "Probe";
            public LanguageCapabilities Capabilities => LanguageCapabilities.Candidates | LanguageCapabilities.ConvertOnCommit;
            public event Action? DataChanged;
            internal bool FailInitialize;
            internal int DisposeCount, EnginesDisposed, ProcessFailures, QueryFailures, QueriesFinished;
            internal TaskCompletionSource<bool>? QueryGate;
            internal Func<CandidateQuery, Task<CandidatePage>>? QueryHandler;
            internal readonly System.Collections.Concurrent.ConcurrentBag<long> QueriesStarted = new System.Collections.Concurrent.ConcurrentBag<long>();
            public ServiceResult Initialize(IResourceProvider resources) => new ServiceResult(!FailInitialize, "probe");
            public Task<ServiceResult> InitializeAsync(IResourceProvider resources, CancellationToken cancellationToken) => Task.FromResult(Initialize(resources));
            public ILanguageEngine CreateEngine() => new Engine(this);
            public LanguageDataSnapshot CaptureData() => new LanguageDataSnapshot(0);
            public ServiceResult UpsertWord(LexiconWord word) { DataChanged?.Invoke(); return new ServiceResult(true); }
            public ServiceResult RemoveWord(string wordId) { DataChanged?.Invoke(); return new ServiceResult(true); }
            public void Dispose() => Interlocked.Increment(ref DisposeCount);
        }
    }
}

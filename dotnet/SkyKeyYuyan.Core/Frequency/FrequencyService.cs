using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 将选择记录、后台加载和有序保存汇合到同一份用户数据中。
    /// </summary>
    public sealed class FrequencyService
    {
        private sealed class Entry
        {
            internal readonly object Gate = new object();
            internal readonly string User, Language;
            internal readonly Dictionary<string, long> Counts = new Dictionary<string, long>(StringComparer.Ordinal);
            internal readonly Dictionary<Guid, HashSet<string>> Records = new Dictionary<Guid, HashSet<string>>();
            internal readonly SemaphoreSlim Writer = new SemaphoreSlim(1, 1);
            internal readonly TaskCompletionSource<FrequencyStoreResult> Ready = new TaskCompletionSource<FrequencyStoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Loading, Loaded, ReplacedUnreadableData;
            internal long Revision, SavedRevision;
            internal Entry(string user, string language) { User = user; Language = language; }
        }
        private static readonly object SharedGate = new object();
        private static FrequencyService? _shared;
        private readonly object _gate = new object();
        private readonly IFrequencyStore _store;
        private readonly Dictionary<string, Dictionary<string, Entry>> _entries = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);

        /// <summary>
        /// 创建使用指定 Provider 的词频服务；运行时统一使用 Shared。
        /// </summary>
        public FrequencyService(IFrequencyStore store) { _store = store ?? throw new ArgumentNullException(nameof(store)); }
        /// <summary>
        /// 进程内共享的词频服务。
        /// </summary>
        public static FrequencyService Shared { get { lock (SharedGate) return _shared ?? (_shared = new FrequencyService(new MemoryFrequencyStore())); } }
        /// <summary>
        /// 首次配置进程存档 Provider；后续调用继续使用原 Provider。
        /// </summary>
        public static bool ConfigureStore(IFrequencyStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            lock (SharedGate) { if (_shared == null) { _shared = new FrequencyService(store); return true; } return ReferenceEquals(_shared._store, store); }
        }
        /// <summary>
        /// 发布已合并的内存词频变化。
        /// </summary>
        public event Action<FrequencyChange>? Changed;
        /// <summary>
        /// 发布独立于正文结果的存储完成状态。
        /// </summary>
        public event Action<FrequencyStoreResult>? StorageCompleted;
        /// <summary>
        /// 发布加载、保存和格式诊断。
        /// </summary>
        public event Action<SkyDiagnostic>? Diagnostic;

        private Entry Get(string userId, string languageId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(languageId)) throw new ArgumentException("用户和语言标识不能为空");
            lock (_gate)
            {
                if (!_entries.TryGetValue(userId, out var languages)) _entries.Add(userId, languages = new Dictionary<string, Entry>(StringComparer.Ordinal));
                if (!languages.TryGetValue(languageId, out var entry)) languages.Add(languageId, entry = new Entry(userId, languageId));
                return entry;
            }
        }
        /// <summary>
        /// 同步预加载；已有后台读取时返回其等待状态。
        /// </summary>
        public FrequencyStoreResult Load(string userId, string languageId)
        {
            var entry = Get(userId, languageId);
            lock (entry.Gate)
            {
                if (entry.Loaded) return entry.Ready.Task.GetAwaiter().GetResult();
                if (entry.Loading) return Pending(entry, "frequency-load");
                entry.Loading = true;
            }
            FrequencyStoreResult? result = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { result = ValidateRead(entry, _store.Load(userId, languageId)); }
                catch (Exception ex) { result = Failure(entry, "frequency-load", ex.Message); }
                if (result.Result.Succeeded) break;
            }
            return ApplyLoaded(entry, result!);
        }
        /// <summary>
        /// 后台加载一次；同一用户及语言的并发请求共享读取结果。
        /// </summary>
        public Task<FrequencyStoreResult> LoadAsync(string userId, string languageId)
        {
            var entry = Get(userId, languageId);
            bool start;
            lock (entry.Gate) { start = !entry.Loading && !entry.Loaded; if (start) entry.Loading = true; }
            if (start) _ = LoadCoreAsync(entry);
            return entry.Ready.Task;
        }
        private async Task LoadCoreAsync(Entry entry)
        {
            FrequencyStoreResult? result = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { result = ValidateRead(entry, await _store.LoadAsync(entry.User, entry.Language, CancellationToken.None).ConfigureAwait(false)); }
                catch (Exception ex) { result = Failure(entry, "frequency-load", ex.Message); }
                if (result.Result.Succeeded) break;
            }
            ApplyLoaded(entry, result!);
        }
        private static FrequencyStoreResult ValidateRead(Entry entry, FrequencyStoreResult result)
        {
            var snapshot = result.Snapshot;
            if (result.Result.Succeeded && snapshot != null && (snapshot.UserId != entry.User || snapshot.LanguageId != entry.Language || snapshot.FormatVersion != 1 || snapshot.Revision < 0 || snapshot.Counts.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)))
                return Failure(entry, "frequency-load", "词频存档身份、版本或计数无效");
            return result;
        }
        private FrequencyStoreResult ApplyLoaded(Entry entry, FrequencyStoreResult result)
        {
            FrequencyChange change;
            lock (entry.Gate)
            {
                if (entry.Loaded) return entry.Ready.Task.GetAwaiter().GetResult();
                if (result.Result.Succeeded && result.Snapshot != null)
                {
                    foreach (var pair in result.Snapshot.Counts) entry.Counts[pair.Key] = SaturatingAdd(entry.Counts.TryGetValue(pair.Key, out var local) ? local : 0, pair.Value);
                    entry.Revision = SaturatingAdd(entry.Revision, result.Snapshot.Revision);
                    entry.SavedRevision = result.Snapshot.Revision;
                }
                else if (!result.Result.Succeeded) { entry.ReplacedUnreadableData = true; entry.Revision++; }
                entry.Loaded = true;
                entry.Ready.TrySetResult(result);
                change = new FrequencyChange(entry.User, entry.Language, entry.Revision);
            }
            if (!result.Result.Succeeded) Notify(Diagnostic, new SkyDiagnostic("frequency-load", result.Result.Reason ?? "词频读取重试失败，使用空存档"));
            Notify(Changed, change);
            return result;
        }
        /// <summary>
        /// 返回已经加载完成的词频副本；加载期间返回基础排序视图。
        /// </summary>
        public IReadOnlyDictionary<string, long> Capture(string userId, string languageId)
        {
            var entry = Get(userId, languageId);
            lock (entry.Gate) return new ReadOnlyDictionary<string, long>(entry.Loaded ? new Dictionary<string, long>(entry.Counts, StringComparer.Ordinal) : new Dictionary<string, long>());
        }
        /// <summary>
        /// 对同一记录累计一次，跨线程操作在用户数据上串行合并。
        /// </summary>
        public bool Record(ChoiceRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Id) || record.Words.Any(word => string.IsNullOrWhiteSpace(word.WordId) || word.Count <= 0)) throw new ArgumentException("选择记录无效", nameof(record));
            var entry = Get(record.UserId, record.LanguageId);
            FrequencyChange change;
            lock (entry.Gate)
            {
                if (!entry.Records.TryGetValue(record.Operation.SessionId, out var records)) entry.Records.Add(record.Operation.SessionId, records = new HashSet<string>(StringComparer.Ordinal));
                if (!records.Add(record.Id)) return false;
                foreach (var word in record.Words) entry.Counts[word.WordId] = SaturatingAdd(entry.Counts.TryGetValue(word.WordId, out var count) ? count : 0, word.Count);
                entry.Revision = SaturatingAdd(entry.Revision, 1);
                change = new FrequencyChange(entry.User, entry.Language, entry.Revision);
            }
            Notify(Changed, change);
            return true;
        }
        /// <summary>
        /// 保存已加载数据；仍在读取或写入时安排后台保存。
        /// </summary>
        public FrequencyStoreResult Save(string userId, string languageId)
        {
            var entry = Get(userId, languageId);
            var loaded = Load(userId, languageId);
            if (loaded.Pending || !entry.Writer.Wait(0)) { _ = SaveAsync(userId, languageId, CancellationToken.None); return Pending(entry, "frequency-save"); }
            try
            {
                var snapshot = CaptureDirty(entry);
                if (snapshot == null) return Success(entry, null);
                FrequencyStoreResult result;
                try { result = _store.Save(snapshot); }
                catch (Exception ex) { result = Failure(entry, "frequency-save", ex.Message); }
                return CompleteSave(entry, snapshot, result);
            }
            finally { entry.Writer.Release(); }
        }
        /// <summary>
        /// 等待读取和合并完成后，按修订顺序保存完整数据。
        /// </summary>
        public async Task<FrequencyStoreResult> SaveAsync(string userId, string languageId, CancellationToken cancellationToken)
        {
            var entry = Get(userId, languageId);
            await LoadAsync(userId, languageId).ConfigureAwait(false);
            try { await entry.Writer.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return new FrequencyStoreResult(userId, languageId, new ServiceResult(false, "frequency-save", "保存已取消", true)); }
            try
            {
                var snapshot = CaptureDirty(entry);
                if (snapshot == null) return Success(entry, null);
                FrequencyStoreResult result;
                try { result = await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { result = new FrequencyStoreResult(userId, languageId, new ServiceResult(false, "frequency-save", "保存已取消", true)); }
                catch (Exception ex) { result = Failure(entry, "frequency-save", ex.Message); }
                return CompleteSave(entry, snapshot, result);
            }
            finally { entry.Writer.Release(); }
        }
        private static FrequencySnapshot? CaptureDirty(Entry entry)
        {
            lock (entry.Gate) return entry.Revision <= entry.SavedRevision ? null : new FrequencySnapshot(entry.User, entry.Language, entry.Revision, entry.Counts);
        }
        private FrequencyStoreResult CompleteSave(Entry entry, FrequencySnapshot snapshot, FrequencyStoreResult result)
        {
            bool replaced = false;
            if (result.Result.Succeeded)
            {
                lock (entry.Gate) { entry.SavedRevision = Math.Max(entry.SavedRevision, snapshot.Revision); replaced = entry.ReplacedUnreadableData; entry.ReplacedUnreadableData = false; }
                if (replaced) Notify(Diagnostic, new SkyDiagnostic("frequency-save", "已保存读取失败后重新累计的词频"));
            }
            else Notify(Diagnostic, new SkyDiagnostic("frequency-save", result.Result.Reason ?? "词频保存失败"));
            Notify(StorageCompleted, result);
            return result;
        }
        /// <summary>
        /// 保存所有已经出现的用户和语言。
        /// </summary>
        public IReadOnlyList<FrequencyStoreResult> SaveAll()
        {
            Entry[] entries;
            lock (_gate) entries = _entries.Values.SelectMany(languages => languages.Values).ToArray();
            return Array.AsReadOnly(entries.Select(entry => Save(entry.User, entry.Language)).ToArray());
        }
        /// <summary>
        /// 等待所有已知用户的待保存数据写入完成。
        /// </summary>
        public async Task<IReadOnlyList<FrequencyStoreResult>> SaveAllAsync(CancellationToken cancellationToken)
        {
            Entry[] entries;
            lock (_gate) entries = _entries.Values.SelectMany(languages => languages.Values).ToArray();
            return Array.AsReadOnly(await Task.WhenAll(entries.Select(entry => SaveAsync(entry.User, entry.Language, cancellationToken))).ConfigureAwait(false));
        }
        /// <summary>
        /// 释放已结束会话的选择记录去重状态。
        /// </summary>
        public void ReleaseSession(Guid sessionId)
        {
            Entry[] entries;
            lock (_gate) entries = _entries.Values.SelectMany(languages => languages.Values).ToArray();
            foreach (var entry in entries) lock (entry.Gate) entry.Records.Remove(sessionId);
        }
        private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
        private static FrequencyStoreResult Failure(Entry entry, string stage, string reason) => new FrequencyStoreResult(entry.User, entry.Language, new ServiceResult(false, stage, reason));
        private static FrequencyStoreResult Pending(Entry entry, string stage) => new FrequencyStoreResult(entry.User, entry.Language, new ServiceResult(false, stage), pending: true);
        private static FrequencyStoreResult Success(Entry entry, FrequencySnapshot? snapshot) => new FrequencyStoreResult(entry.User, entry.Language, new ServiceResult(true, "frequency-save"), snapshot);
        private static void Notify<T>(Action<T>? handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch { /* 存储观察者的异常不改变已完成的读写。 */ }
        }
    }
}

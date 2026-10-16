using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 按用户和语言保存的不可变词频修订。
    /// </summary>
    public sealed class FrequencySnapshot
    {
        /// <summary>
        /// 创建可持久化的完整词频快照。
        /// </summary>
        public FrequencySnapshot(string userId, string languageId, long revision, IReadOnlyDictionary<string, long> counts, int formatVersion = 1)
        { UserId = userId; LanguageId = languageId; Revision = revision; FormatVersion = formatVersion; Counts = new ReadOnlyDictionary<string, long>(counts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)); }
        /// <summary>
        /// 存档所属用户。
        /// </summary>
        public string UserId { get; }
        /// <summary>
        /// 语言命名范围。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 存档修订号。
        /// </summary>
        public long Revision { get; }
        /// <summary>
        /// 数据格式版本。
        /// </summary>
        public int FormatVersion { get; }
        /// <summary>
        /// 稳定词条标识到累计选择次数的映射。
        /// </summary>
        public IReadOnlyDictionary<string, long> Counts { get; }
    }
    /// <summary>
    /// 存档读取或保存的结果。
    /// </summary>
    public sealed class FrequencyStoreResult
    {
        /// <summary>
        /// 创建具有用户和语言归属的存档结果。
        /// </summary>
        public FrequencyStoreResult(string userId, string languageId, ServiceResult result, FrequencySnapshot? snapshot = null, bool pending = false)
        { UserId = userId; LanguageId = languageId; Result = result; Snapshot = snapshot; Pending = pending; }
        /// <summary>
        /// 用户标识。
        /// </summary>
        public string UserId { get; }
        /// <summary>
        /// 语言标识。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 读写结果及诊断原因。
        /// </summary>
        public ServiceResult Result { get; }
        /// <summary>
        /// 已读取或保存的完整快照。
        /// </summary>
        public FrequencySnapshot? Snapshot { get; }
        /// <summary>
        /// 保存是否仍在后台等待完成。
        /// </summary>
        public bool Pending { get; }
    }
    /// <summary>
    /// 结构化词频存储，由宿主管理生命周期。
    /// </summary>
    public interface IFrequencyStore
    {
        /// <summary>
        /// 同步读取指定用户及语言，缺失存档返回成功和空快照。
        /// </summary>
        FrequencyStoreResult Load(string userId, string languageId);
        /// <summary>
        /// 异步读取指定用户及语言。
        /// </summary>
        Task<FrequencyStoreResult> LoadAsync(string userId, string languageId, CancellationToken cancellationToken);
        /// <summary>
        /// 同步保存指定修订的完整数据。
        /// </summary>
        FrequencyStoreResult Save(FrequencySnapshot snapshot);
        /// <summary>
        /// 异步保存指定修订的完整数据。
        /// </summary>
        Task<FrequencyStoreResult> SaveAsync(FrequencySnapshot snapshot, CancellationToken cancellationToken);
    }
    /// <summary>
    /// 具有操作归属的一次有效词条选择。
    /// </summary>
    public sealed class ChoiceRecord
    {
        /// <summary>
        /// 创建可去重的词条选择记录。
        /// </summary>
        public ChoiceRecord(string id, OperationId operation, string userId, string languageId, IEnumerable<WordUsage> words)
        { Id = id; Operation = operation; UserId = userId; LanguageId = languageId; Words = Array.AsReadOnly(words.ToArray()); }
        /// <summary>
        /// 恢复和重试沿用的记录标识。
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// 所属会话及操作。
        /// </summary>
        public OperationId Operation { get; }
        /// <summary>
        /// 用户标识。
        /// </summary>
        public string UserId { get; }
        /// <summary>
        /// 语言标识。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 被选择的已有词条。
        /// </summary>
        public IReadOnlyList<WordUsage> Words { get; }
    }
    /// <summary>
    /// 用户词频发生变化的通知。
    /// </summary>
    public sealed class FrequencyChange
    {
        internal FrequencyChange(string userId, string languageId, long revision) { UserId = userId; LanguageId = languageId; Revision = revision; }
        /// <summary>
        /// 用户标识。
        /// </summary>
        public string UserId { get; }
        /// <summary>
        /// 语言标识。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 新的内存修订号。
        /// </summary>
        public long Revision { get; }
    }
    /// <summary>
    /// 线程安全的内存存档 Provider。
    /// </summary>
    public sealed class MemoryFrequencyStore : IFrequencyStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Dictionary<string, FrequencySnapshot>> _users = new Dictionary<string, Dictionary<string, FrequencySnapshot>>(StringComparer.Ordinal);
        /// <inheritdoc />
        public FrequencyStoreResult Load(string userId, string languageId)
        {
            lock (_gate)
            {
                FrequencySnapshot? snapshot = null;
                if (_users.TryGetValue(userId, out var languages)) languages.TryGetValue(languageId, out snapshot);
                return new FrequencyStoreResult(userId, languageId, new ServiceResult(true, "frequency-load"), snapshot);
            }
        }
        /// <inheritdoc />
        public Task<FrequencyStoreResult> LoadAsync(string userId, string languageId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Load(userId, languageId)); }
        /// <inheritdoc />
        public FrequencyStoreResult Save(FrequencySnapshot snapshot)
        {
            lock (_gate)
            {
                if (!_users.TryGetValue(snapshot.UserId, out var languages)) _users.Add(snapshot.UserId, languages = new Dictionary<string, FrequencySnapshot>(StringComparer.Ordinal));
                languages[snapshot.LanguageId] = snapshot;
                return new FrequencyStoreResult(snapshot.UserId, snapshot.LanguageId, new ServiceResult(true, "frequency-save"), snapshot);
            }
        }
        /// <inheritdoc />
        public Task<FrequencyStoreResult> SaveAsync(FrequencySnapshot snapshot, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Save(snapshot)); }
    }
}

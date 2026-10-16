using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 从版本化文本资源准备词典，并发布自定义词条的完整新视图。
    /// </summary>
    public abstract class LexiconModule : ILanguageModule
    {
        private readonly struct WordKey : IEquatable<WordKey>
        {
            private readonly string _text, _spelling;
            internal WordKey(LexiconWord word) { _text = word.Text; _spelling = word.Spelling; }
            public bool Equals(WordKey other) => _text == other._text && _spelling == other._spelling;
            public override bool Equals(object? other) => other is WordKey key && Equals(key);
            public override int GetHashCode() => unchecked(StringComparer.Ordinal.GetHashCode(_text) * 397 ^ StringComparer.Ordinal.GetHashCode(_spelling));
        }
        private readonly object _gate = new object();
        private readonly LexiconWord[]? _seed;
        private readonly Dictionary<string, LexiconWord> _custom = new Dictionary<string, LexiconWord>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _customOrder = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<WordKey, string> _canonicalIds = new Dictionary<WordKey, string>();
        private LexiconWord[] _base = Array.Empty<LexiconWord>();
        private LanguageDataSnapshot _data = new LanguageDataSnapshot(0);
        private long _version, _order;
        private bool _initialized;

        /// <summary>
        /// 配置资源标识或显式内存词典。
        /// </summary>
        protected LexiconModule(string resourceId, IEnumerable<LexiconWord>? words = null) { ResourceId = resourceId; _seed = words?.ToArray(); }
        /// <summary>
        /// Provider 中的资源标识。
        /// </summary>
        public string ResourceId { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract string LanguageId { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract string EngineId { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract string DisplayName { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract LanguageCapabilities Capabilities { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public event Action? DataChanged;
        /// <summary>
        /// 规范化词条匹配拼写。
        /// </summary>
        protected abstract string NormalizeSpelling(string spelling);
        /// <summary>
        /// 为新数据视图创建只读索引。
        /// </summary>
        protected abstract object BuildIndex(IReadOnlyList<LexiconWord> words);
        /// <summary>
        /// 创建具体语言的可变组合状态。
        /// </summary>
        protected abstract ILanguageEngine CreateLanguageEngine();
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult Initialize(IResourceProvider resources)
        {
            try { return Prepare(_seed ?? Parse(resources.Read(ResourceId))); }
            catch (Exception ex) { return new ServiceResult(false, "resource:" + EngineId, ex.Message); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public async Task<ServiceResult> InitializeAsync(IResourceProvider resources, CancellationToken cancellationToken)
        {
            try
            {
                byte[]? bytes = _seed == null ? await resources.ReadAsync(ResourceId, cancellationToken).ConfigureAwait(false) : null;
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var words = _seed ?? Parse(bytes!);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Prepare(words);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return new ServiceResult(false, "resource:" + EngineId, "资源读取已取消", true); }
            catch (Exception ex) { return new ServiceResult(false, "resource:" + EngineId, ex.Message); }
        }
        private LexiconWord[] Parse(byte[] bytes)
        {
            using (var reader = new StreamReader(new MemoryStream(bytes, false), new UTF8Encoding(false, true), false, 4096))
            {
                string[]? header = reader.ReadLine()?.TrimStart('\ufeff').Split('\t');
                if (header == null || header.Length != 4 || header[0] != "SKY" || header[1] != "1" || header[2] != LanguageId || string.IsNullOrWhiteSpace(header[3])) throw new InvalidDataException("语言资源标识或格式版本不兼容");
                var words = new List<LexiconWord>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    int first = line.IndexOf('\t'), second = first < 0 ? -1 : line.IndexOf('\t', first + 1), third = second < 0 ? -1 : line.IndexOf('\t', second + 1);
                    if (first < 1 || second < 0 || third < 0 || line.IndexOf('\t', third + 1) >= 0 || !double.TryParse(line.Substring(third + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double weight)) throw new InvalidDataException("词条记录结构无效");
                    string text = line.Substring(first + 1, second - first - 1);
                    int spellingLength = third - second - 1;
                    string spelling = text.Length == spellingLength && string.CompareOrdinal(line, first + 1, line, second + 1, spellingLength) == 0 ? text : line.Substring(second + 1, spellingLength);
                    words.Add(new LexiconWord(line.Substring(0, first), text, spelling, weight));
                }
                return words.ToArray();
            }
        }
        private ServiceResult Prepare(LexiconWord[] words)
        {
            lock (_gate)
            {
                if (_initialized) return new ServiceResult(true, "language");
                if (words.Select(word => word.Id).Distinct(StringComparer.Ordinal).Count() != words.Length) return new ServiceResult(false, "resource:" + EngineId, "词条标识重复");
                _base = words.Select(word =>
                {
                    string spelling = NormalizeSpelling(word.Spelling);
                    return spelling == word.Spelling ? word : new LexiconWord(word.Id, word.Text, spelling, word.Weight);
                }).ToArray();
                Rebuild(); _initialized = true;
                return new ServiceResult(true, "language");
            }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public ILanguageEngine CreateEngine()
        {
            lock (_gate) { if (!_initialized) throw new InvalidOperationException("语言模块尚未初始化"); return CreateLanguageEngine(); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public LanguageDataSnapshot CaptureData() { lock (_gate) return _data; }
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult UpsertWord(LexiconWord word)
        {
            if (word == null) throw new ArgumentNullException(nameof(word));
            try
            {
                lock (_gate)
                {
                    var normalized = new LexiconWord(word.Id, word.Text, NormalizeSpelling(word.Spelling), word.Weight);
                    if (_base.Any(item => item.Id == normalized.Id && !new WordKey(item).Equals(new WordKey(normalized)))) return new ServiceResult(false, "dictionary", "宿主词条标识与基础词条冲突");
                    _custom[word.Id] = normalized; _customOrder[word.Id] = ++_order;
                    Rebuild();
                }
                NotifyDataChanged(); return new ServiceResult(true, "dictionary");
            }
            catch (Exception ex) { return new ServiceResult(false, "dictionary", ex.Message); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult RemoveWord(string wordId)
        {
            lock (_gate)
            {
                if (!_custom.Remove(wordId)) return new ServiceResult(false, "dictionary", "宿主词条不存在");
                _customOrder.Remove(wordId); Rebuild();
            }
            NotifyDataChanged(); return new ServiceResult(true, "dictionary");
        }
        private void Rebuild()
        {
            var effective = new Dictionary<WordKey, LexiconWord>(_base.Length);
            foreach (var word in _base) effective[new WordKey(word)] = word;
            foreach (var word in _custom.Values.OrderBy(word => _customOrder[word.Id]))
            {
                var key = new WordKey(word);
                // 基础词条的身份来自当前视图，仅为宿主词条保留删除后的身份映射。
                string canonical;
                if (effective.TryGetValue(key, out var existing)) canonical = existing.Id;
                else if (!_canonicalIds.TryGetValue(key, out canonical)) canonical = word.Id;
                _canonicalIds[key] = canonical;
                effective[key] = new LexiconWord(canonical, word.Text, word.Spelling, word.Weight);
            }
            var words = effective.Values.ToArray();
            var index = BuildIndex(words);
            _data = new LanguageDataSnapshot(++_version, words, index);
        }
        private void NotifyDataChanged()
        {
            if (DataChanged == null) return;
            foreach (Action handler in DataChanged.GetInvocationList()) try { handler(); } catch { /* 新数据视图已经完整发布。 */ }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { lock (_gate) { _initialized = false; _base = Array.Empty<LexiconWord>(); _data = new LanguageDataSnapshot(++_version); } }
    }
}

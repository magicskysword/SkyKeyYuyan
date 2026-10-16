using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 按规范化拼写排序的不可变索引，支持完整匹配和前缀范围查询。
    /// </summary>
    public sealed class SpellingIndex
    {
        private readonly KeyValuePair<string, LexiconWord>[] _entries;
        /// <summary>
        /// 创建只读索引和词典权重总量。
        /// </summary>
        public SpellingIndex(IEnumerable<LexiconWord> words, Func<string, string> keySelector)
        {
            _entries = words.Select(word => new KeyValuePair<string, LexiconWord>(keySelector(word.Spelling), word)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ThenBy(pair => pair.Value.Id, StringComparer.Ordinal).ToArray();
            MaxKeyLength = _entries.Length == 0 ? 0 : _entries.Max(pair => pair.Key.Length);
            double max = _entries.Length == 0 ? 1 : Math.Max(1, _entries.Max(pair => pair.Value.Weight));
            LogTotalWeight = Math.Log(max) + Math.Log(Math.Max(1, _entries.Sum(pair => (pair.Value.Weight + 1) / max)));
        }
        /// <summary>
        /// 索引内最长拼写。
        /// </summary>
        public int MaxKeyLength { get; }
        /// <summary>
        /// 用于路径评分的总权重自然对数。
        /// </summary>
        public double LogTotalWeight { get; }
        /// <summary>
        /// 返回拼写完全相同的词条。
        /// </summary>
        public IEnumerable<LexiconWord> Exact(string key)
        {
            for (int index = LowerBound(key); index < _entries.Length && _entries[index].Key == key; index++) yield return _entries[index].Value;
        }
        /// <summary>
        /// 返回具有指定前缀的词条。
        /// </summary>
        public IEnumerable<LexiconWord> Prefix(string prefix)
        {
            for (int index = LowerBound(prefix); index < _entries.Length && _entries[index].Key.StartsWith(prefix, StringComparison.Ordinal); index++) yield return _entries[index].Value;
        }
        private int LowerBound(string key)
        {
            int low = 0, high = _entries.Length;
            while (low < high) { int middle = low + (high - low) / 2; if (StringComparer.Ordinal.Compare(_entries[middle].Key, key) < 0) low = middle + 1; else high = middle; }
            return low;
        }
    }
}

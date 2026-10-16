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
            var source = words as IReadOnlyCollection<LexiconWord> ?? words.ToArray();
            _entries = new KeyValuePair<string, LexiconWord>[source.Count];
            int position = 0, longest = 0; double max = 1;
            foreach (var word in source)
            {
                string key = keySelector(word.Spelling);
                _entries[position++] = new KeyValuePair<string, LexiconWord>(key, word);
                longest = Math.Max(longest, key.Length); max = Math.Max(max, word.Weight);
            }
            Array.Sort(_entries, (left, right) =>
            {
                int key = StringComparer.Ordinal.Compare(left.Key, right.Key);
                return key != 0 ? key : StringComparer.Ordinal.Compare(left.Value.Id, right.Value.Id);
            });
            double total = 0;
            foreach (var entry in _entries) total += (entry.Value.Weight + 1) / max;
            MaxKeyLength = longest;
            LogTotalWeight = Math.Log(max) + Math.Log(Math.Max(1, total));
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

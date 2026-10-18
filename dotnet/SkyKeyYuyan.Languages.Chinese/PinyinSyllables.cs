using System.Collections.Generic;
using System.Text;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese
{
    internal sealed class PinyinSyllables
    {
        private enum SegmentKind : byte { Unknown, Syllable, Abbreviation, Prefix, End }
        private sealed class Node
        {
            internal readonly Node?[] Children = new Node?[26];
            internal bool Complete;
        }

        private readonly Node _root = new Node();

        internal PinyinSyllables(IReadOnlyList<LexiconWord> words)
        {
            foreach (var word in words)
            {
                var node = _root;
                foreach (char value in word.Spelling)
                {
                    if (value == ' ') { node.Complete = true; node = _root; }
                    else node = node.Children[value - 'a'] ?? (node.Children[value - 'a'] = new Node());
                }
                node.Complete = true;
            }
        }

        internal string Render(string raw)
        {
            if (raw.Length < 2) return raw;
            var input = new PinyinSpelling.Input(raw);
            int length = input.Key.Length;
            if (length < 2) return raw;
            var next = new int[length + 1];
            var unknown = new int[length + 1];
            var segments = new int[length + 1];
            var penalties = new int[length + 1];
            var kinds = new SegmentKind[length + 1];
            kinds[length] = SegmentKind.End;
            int limit = length;
            for (int start = length - 1; start >= 0; start--)
            {
                bool boundary = input.Boundaries.Contains(start + 1);
                if (boundary) limit = start + 1;
                next[start] = start + 1;
                unknown[start] = unknown[start + 1] + 1;
                penalties[start] = penalties[start + 1];
                segments[start] = segments[start + 1] + (kinds[start + 1] == SegmentKind.Unknown && !boundary ? 0 : 1);
                var node = _root;
                for (int end = start; end < limit; end++)
                {
                    char value = input.Key[end];
                    if (value < 'a' || value > 'z' || (node = node.Children[value - 'a']) == null) break;
                    bool abbreviation = PinyinSpelling.IsAbbreviation(input.Key, start, end - start + 1);
                    if (!node.Complete && !abbreviation && (end + 1 != length || input.Boundaries.Contains(length))) continue;
                    int tail = end + 1, count = segments[tail] + 1;
                    int penalty = penalties[tail] + (node.Complete ? 0 : abbreviation ? 1 : 2);
                    // 同等覆盖下优先完整音节，再比较片段数和前音节长度。
                    if (unknown[tail] > unknown[start]) continue;
                    if (unknown[tail] == unknown[start] && (penalty > penalties[start] || penalty == penalties[start] && count > segments[start])) continue;
                    if (unknown[tail] == unknown[start] && penalty == penalties[start] && count == segments[start] && tail < next[start]) continue;
                    unknown[start] = unknown[tail]; segments[start] = count; penalties[start] = penalty; next[start] = tail;
                    kinds[start] = node.Complete ? SegmentKind.Syllable : abbreviation ? SegmentKind.Abbreviation : SegmentKind.Prefix;
                }
            }

            StringBuilder? result = null;
            int copied = 0; var previous = SegmentKind.Unknown;
            for (int start = 0; start < length; start = next[start])
            {
                // 连续的未知拼写保持为一个片段，原始分隔符保留原样。
                if (start > 0 && (IsSyllable(previous) || IsSyllable(kinds[start])) && !input.Boundaries.Contains(start))
                {
                    int offset = input.Ends[start - 1];
                    if (result == null) result = new StringBuilder(raw.Length + length / 2);
                    result.Append(raw, copied, offset - copied).Append('\''); copied = offset;
                }
                previous = kinds[start];
            }
            return result == null ? raw : result.Append(raw, copied, raw.Length - copied).ToString();
        }

        private static bool IsSyllable(SegmentKind kind) => kind == SegmentKind.Syllable || kind == SegmentKind.Abbreviation;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese
{
    internal sealed class PinyinLexiconIndex
    {
        internal const double AbbreviationPenalty = 2.3;
        internal const double CompletionPenalty = 16;

        internal readonly struct WordMatch
        {
            internal readonly LexiconWord Word;
            internal readonly int End;
            internal readonly double Penalty;
            internal WordMatch(LexiconWord word, int end, double penalty)
            { Word = word; End = end; Penalty = penalty; }
        }

        internal readonly struct Edge
        {
            internal readonly int End, First, Count;
            internal readonly double Penalty;
            internal Edge(int end, int first, int count, double penalty)
            { End = end; First = first; Count = count; Penalty = penalty; }
        }

        internal sealed class Graph
        {
            internal readonly PinyinSpelling.Input Input;
            internal readonly Edge[][] Edges;
            internal Graph(PinyinSpelling.Input input, Edge[][] edges) { Input = input; Edges = edges; }
        }

        private struct Prefix
        {
            internal int First, Count, Complete;
            internal Prefix(int first) { First = first; Count = 0; Complete = -1; }
        }

        private struct Node
        {
            internal int Syllable, FirstWord, EndWord, WordCount, FirstChild, ChildCount;
            internal Node(int syllable, int firstWord, int endWord)
            { Syllable = syllable; FirstWord = firstWord; EndWord = endWord; WordCount = 0; FirstChild = 0; ChildCount = 0; }
        }

        private readonly struct EncodedWord
        {
            internal readonly LexiconWord Word;
            internal readonly int[] Code;
            internal EncodedWord(LexiconWord word, int[] code) { Word = word; Code = code; }
        }

        private readonly Dictionary<string, Prefix> _prefixes = new Dictionary<string, Prefix>(StringComparer.Ordinal);
        private readonly Node[] _nodes;
        private readonly LexiconWord[] _words;
        private readonly int _maxSyllableLength;

        internal PinyinLexiconIndex(IReadOnlyList<LexiconWord> words)
        {
            var syllables = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in words)
                foreach (var syllable in Split(word.Spelling)) syllables.Add(syllable);
            var ordered = syllables.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var identities = new Dictionary<string, int>(ordered.Length, StringComparer.Ordinal);
            for (int index = 0; index < ordered.Length; index++)
            {
                string spelling = ordered[index]; identities.Add(spelling, index);
                _maxSyllableLength = Math.Max(_maxSyllableLength, spelling.Length);
                for (int length = 1; length <= spelling.Length; length++)
                {
                    string key = spelling.Substring(0, length);
                    if (!_prefixes.TryGetValue(key, out var prefix)) prefix = new Prefix(index);
                    prefix.Count++;
                    if (length == spelling.Length) prefix.Complete = index;
                    _prefixes[key] = prefix;
                }
            }

            var encoded = new EncodedWord[words.Count];
            for (int index = 0; index < words.Count; index++)
                encoded[index] = new EncodedWord(words[index], Split(words[index].Spelling).Select(value => identities[value]).ToArray());
            Array.Sort(encoded, (left, right) =>
            {
                for (int position = 0; position < Math.Min(left.Code.Length, right.Code.Length); position++)
                {
                    int result = left.Code[position].CompareTo(right.Code[position]);
                    if (result != 0) return result;
                }
                int length = left.Code.Length.CompareTo(right.Code.Length);
                return length != 0 ? length : StringComparer.Ordinal.Compare(left.Word.Id, right.Word.Id);
            });

            var nodes = new List<Node>(words.Count + 1) { new Node(-1, 0, encoded.Length) };
            var depths = new List<int>(words.Count + 1) { 0 };
            // 同一父节点的子节点连续保存，查询可以按音节范围二分定位。
            for (int index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index]; int depth = depths[index], position = node.FirstWord;
                while (position < node.EndWord && encoded[position].Code.Length == depth) position++;
                node.WordCount = position - node.FirstWord;
                node.FirstChild = nodes.Count;
                while (position < node.EndWord)
                {
                    int first = position, syllable = encoded[position].Code[depth];
                    while (position < node.EndWord && encoded[position].Code[depth] == syllable) position++;
                    nodes.Add(new Node(syllable, first, position)); depths.Add(depth + 1);
                }
                node.ChildCount = nodes.Count - node.FirstChild;
                nodes[index] = node;
            }
            _nodes = nodes.ToArray();
            _words = encoded.Select(value => value.Word).ToArray();
        }

        private static IEnumerable<string> Split(string spelling)
        {
            int start = 0;
            while (start < spelling.Length)
            {
                int end = spelling.IndexOf(' ', start);
                if (end < 0) end = spelling.Length;
                yield return spelling.Substring(start, end - start);
                start = end + 1;
            }
        }

        internal Graph Parse(PinyinSpelling.Input input, CancellationToken cancellationToken)
        {
            int length = input.Key.Length, limit = length;
            var edges = new Edge[length][];
            for (int start = length - 1; start >= 0; start--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Boundaries.Contains(start + 1)) limit = start + 1;
                var matches = new List<Edge>(); int completeEnd = start;
                for (int count = 1; count <= Math.Min(_maxSyllableLength, limit - start); count++)
                {
                    string key = input.Key.Substring(start, count);
                    if (!_prefixes.TryGetValue(key, out var prefix)) break;
                    int end = start + count;
                    if (prefix.Complete >= 0) { matches.Add(new Edge(end, prefix.Complete, 1, 0)); completeEnd = end; }
                    bool abbreviation = PinyinSpelling.IsAbbreviation(key, 0, count);
                    if (abbreviation) matches.Add(new Edge(end, prefix.First, prefix.Count, AbbreviationPenalty));
                    else if (end == length && !input.Boundaries.Contains(length))
                        matches.Add(new Edge(end, prefix.First, prefix.Count, CompletionPenalty));
                }
                // 完整音节覆盖的字母优先保持为完整拼写。
                for (int index = matches.Count - 1; index >= 0; index--)
                    if (matches[index].Penalty == AbbreviationPenalty && matches[index].End < completeEnd) matches.RemoveAt(index);
                edges[start] = matches.ToArray();
            }
            return new Graph(input, edges);
        }

        internal IEnumerable<WordMatch> MatchWords(Graph graph, int start, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = graph.Input.Key.Length;
            var states = new Dictionary<int, double>?[length + 1];
            states[start] = new Dictionary<int, double> { [0] = 0 };
            // 输入位置只向前移动；处理一个位置时，到达该处的最低代价已确定。
            for (int position = start; position <= length; position++)
            {
                if (states[position] == null) continue;
                foreach (var state in states[position]!)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var node = _nodes[state.Key];
                    for (int word = node.FirstWord; word < node.FirstWord + node.WordCount; word++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return new WordMatch(_words[word], position, state.Value);
                    }
                    if (position == length || node.ChildCount == 0) continue;
                    foreach (var edge in graph.Edges[position])
                    {
                        int end = node.FirstChild + node.ChildCount;
                        for (int child = LowerBound(node, edge.First); child < end && _nodes[child].Syllable < edge.First + edge.Count; child++)
                        {
                            var next = states[edge.End] ?? (states[edge.End] = new Dictionary<int, double>());
                            double penalty = state.Value + edge.Penalty;
                            if (!next.TryGetValue(child, out double previous) || penalty < previous) next[child] = penalty;
                        }
                    }
                }
            }
        }

        private int LowerBound(Node node, int syllable)
        {
            int start = node.FirstChild, end = start + node.ChildCount;
            while (start < end)
            {
                int middle = start + (end - start) / 2;
                if (_nodes[middle].Syllable < syllable) start = middle + 1;
                else end = middle;
            }
            return start;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Internal;

namespace SkyKeyYuyan.Languages.Chinese
{
    /// <summary>
    /// 简体中文连续全拼、分段选择及基本整句转换模块。
    /// </summary>
    public sealed class ChinesePinyinModule : LexiconModule, IKeyboardLayoutSource
    {
        /// <summary>
        /// 从 Provider 资源或显式词条准备中文词典。
        /// </summary>
        public ChinesePinyinModule(IEnumerable<LexiconWord>? words = null, string resourceId = "chinese/lexicon.bytes") : base(resourceId, words) { }
        /// <inheritdoc>
        /// </inheritdoc>
        public override string LanguageId => "zh-Hans";
        /// <inheritdoc>
        /// </inheritdoc>
        public override string EngineId => "zh.full-pinyin";
        /// <inheritdoc>
        /// </inheritdoc>
        public override string DisplayName => "简体中文";
        /// <inheritdoc>
        /// </inheritdoc>
        public override LanguageCapabilities Capabilities => LanguageCapabilities.Candidates | LanguageCapabilities.ConvertOnCommit;
        /// <inheritdoc>
        /// </inheritdoc>
        public KeyboardLayout LoadKeyboard(IResourceProvider resources) => KeyboardLayout.Parse(resources.Read("chinese/keyboard.bytes"), LanguageId);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string NormalizeSpelling(string spelling) => PinyinSpelling.Normalize(spelling);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override object BuildIndex(IReadOnlyList<LexiconWord> words) => new Index(words);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override ILanguageEngine CreateLanguageEngine() => new Engine();

        private sealed class Index
        {
            internal readonly SpellingIndex Words;
            internal readonly HashSet<string> MultipleReadings = new HashSet<string>(StringComparer.Ordinal);
            internal Index(IReadOnlyList<LexiconWord> words)
            {
                Words = new SpellingIndex(words, PinyinSpelling.Flatten);
                var texts = new HashSet<string>(StringComparer.Ordinal);
                foreach (var word in words)
                    if (!texts.Add(word.Text)) MultipleReadings.Add(word.Text);
            }
            internal bool Fits(LexiconWord word, PinyinSpelling.Input input, int start, int end)
            {
                foreach (int boundary in input.Boundaries)
                {
                    if (boundary <= start || boundary >= end) continue;
                    int wanted = boundary - start, position = 0; bool found = false;
                    foreach (char value in word.Spelling)
                    {
                        if (value == ' ') { if (position == wanted) { found = true; break; } }
                        else position++;
                    }
                    if (!found && position != wanted) return false;
                }
                return true;
            }
        }
        private sealed class Path
        {
            internal readonly string Text;
            internal readonly Path? Previous;
            internal readonly LexiconWord? Word;
            internal readonly double Score;
            internal Path(Path? previous = null, LexiconWord? word = null, double score = 0)
            { Previous = previous; Word = word; Text = (previous?.Text ?? "") + (word?.Text ?? ""); Score = score; }
            internal WordUsage[] Usage()
            {
                var words = new List<WordUsage>();
                for (Path? path = this; path?.Word != null; path = path.Previous) words.Add(new WordUsage(path.Word.Id));
                words.Reverse(); return words.ToArray();
            }
        }
        private sealed class Engine : TokenLanguageEngine
        {
            private const int BeamWidth = 8;
            private readonly struct CandidateKey : IEquatable<CandidateKey>
            {
                private readonly string _text;
                private readonly int _consumed;
                internal CandidateKey(string text, int consumed) { _text = text; _consumed = consumed; }
                public bool Equals(CandidateKey other) => _consumed == other._consumed && _text == other._text;
                public override bool Equals(object? other) => other is CandidateKey key && Equals(key);
                public override int GetHashCode() => unchecked(StringComparer.Ordinal.GetHashCode(_text) * 397 ^ _consumed);
            }
            private readonly struct Match
            {
                internal readonly string Text;
                internal readonly int Consumed;
                internal readonly double Score;
                private readonly LexiconWord? _word;
                private readonly Path? _path;
                internal Match(LexiconWord word, int consumed, double score)
                { Text = word.Text; Consumed = consumed; Score = score; _word = word; _path = null; }
                internal Match(Path path, int consumed)
                { Text = path.Text; Consumed = consumed; Score = path.Score; _word = null; _path = path; }
                internal Candidate Build(int rank) => new Candidate(rank.ToString(CultureInfo.InvariantCulture), Text, Consumed, _word != null ? new[] { new WordUsage(_word.Id) } : _path!.Usage(), Score);
            }
            private readonly struct ScoredWord
            {
                internal readonly LexiconWord Word;
                internal readonly double Score;
                internal ScoredWord(LexiconWord word, double score) { Word = word; Score = score; }
            }
            internal Engine() : base("zh.full-pinyin") { }
            protected override bool UsesCandidates => true;
            protected override bool IsCompositionCharacter(char value) => PinyinSpelling.IsInput(value);
            public override Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken) => Task.Run(() => Query(query, cancellationToken), cancellationToken);
            private static CandidatePage Query(CandidateQuery query, CancellationToken cancellationToken)
            {
                var input = new PinyinSpelling.Input(query.Composition.RawText);
                if (input.Key.Length == 0) return new CandidatePage(query, Array.Empty<Candidate>());
                var index = query.Data.Index as Index ?? new Index(query.Data.Words);
                var special = new Dictionary<CandidateKey, Match>();
                var candidates = new RankedWindow<Match>((long)query.Offset + query.PageSize + 1, (left, right) =>
                {
                    int consumed = right.Consumed.CompareTo(left.Consumed);
                    if (consumed != 0) return consumed;
                    int score = right.Score.CompareTo(left.Score);
                    return score != 0 ? score : StringComparer.Ordinal.Compare(left.Text, right.Text);
                });
                void Merge(Match match)
                {
                    var key = new CandidateKey(match.Text, match.Consumed);
                    if (!special.TryGetValue(key, out var previous) || match.Score > previous.Score) special[key] = match;
                }
                var paths = new List<Path>?[input.Key.Length + 1];
                paths[0] = new List<Path> { new Path() };
                var matches = new List<ScoredWord>(BeamWidth);

                for (int start = 0; start < input.Key.Length; start++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (paths[start] == null) continue;
                    for (int length = 1; length <= Math.Min(index.Words.MaxKeyLength, input.Key.Length - start); length++)
                    {
                        int end = start + length;
                        matches.Clear();
                        foreach (var word in index.Words.Exact(input.Key.Substring(start, length)))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!index.Fits(word, input, start, end)) continue;
                            double score = Score(word, query, index); int position = 0;
                            while (position < matches.Count && matches[position].Score >= score) position++;
                            if (position >= BeamWidth) continue;
                            matches.Insert(position, new ScoredWord(word, score));
                            if (matches.Count > BeamWidth) matches.RemoveAt(BeamWidth);
                        }
                        foreach (var match in matches)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (start == 0) Merge(new Match(match.Word, input.Consumed(end), match.Score));
                            var next = paths[end] ?? (paths[end] = new List<Path>());
                            foreach (var path in paths[start]!) MergePath(next, new Path(path, match.Word, path.Score + match.Score));
                            TrimPaths(next);
                        }
                    }
                }
                if (paths[input.Key.Length] != null)
                    foreach (var path in paths[input.Key.Length]!) Merge(new Match(path, input.Raw.Length));
                foreach (var word in index.Words.Prefix(input.Key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!index.Fits(word, input, 0, input.Key.Length)) continue;
                    int keyLength = 0;
                    foreach (char value in word.Spelling) if (value != ' ') keyLength++;
                    var match = new Match(word, input.Raw.Length, Score(word, query, index) - (keyLength > input.Key.Length ? 16 : 0));
                    var key = new CandidateKey(match.Text, match.Consumed);
                    if (special.ContainsKey(key) || index.MultipleReadings.Contains(word.Text)) Merge(match);
                    else candidates.Add(match);
                }
                foreach (var match in special.Values) candidates.Add(match);
                return new CandidatePage(query, candidates.GetPage(query.Offset, query.PageSize).Select((match, rank) => match.Build(query.Offset + rank)), (long)query.Offset + query.PageSize < candidates.Seen);
            }
            private static void MergePath(List<Path> paths, Path candidate)
            {
                for (int index = 0; index < paths.Count; index++)
                {
                    if (paths[index].Text != candidate.Text) continue;
                    if (candidate.Score > paths[index].Score) paths[index] = candidate;
                    return;
                }
                paths.Add(candidate);
            }
            private static void TrimPaths(List<Path> paths)
            {
                // 相同分值沿用路径首次出现的顺序，避免改变有限宽度搜索的取舍。
                for (int index = 1; index < paths.Count; index++)
                {
                    var value = paths[index]; int position = index;
                    while (position > 0 && paths[position - 1].Score < value.Score) { paths[position] = paths[position - 1]; position--; }
                    paths[position] = value;
                }
                if (paths.Count > BeamWidth) paths.RemoveRange(BeamWidth, paths.Count - BeamWidth);
            }
            private static double Score(LexiconWord word, CandidateQuery query, Index index)
            {
                query.Frequencies.TryGetValue(word.Id, out long count);
                return Math.Log(1 + word.Weight) - index.Words.LogTotalWeight + 4 * Math.Log(1d + count);
            }
        }
    }
}

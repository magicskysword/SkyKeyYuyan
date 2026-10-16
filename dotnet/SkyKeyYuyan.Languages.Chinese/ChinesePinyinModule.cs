using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese
{
    /// <summary>
    /// 简体中文连续全拼、分段选择及基本整句转换模块。
    /// </summary>
    public sealed class ChinesePinyinModule : LexiconModule
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
            private readonly Dictionary<string, int[]> _boundaries = new Dictionary<string, int[]>(StringComparer.Ordinal);
            internal Index(IEnumerable<LexiconWord> words)
            {
                var list = words.ToArray(); Words = new SpellingIndex(list, PinyinSpelling.Flatten);
                foreach (var word in list)
                {
                    int position = 0;
                    var boundaries = new List<int>();
                    foreach (string syllable in word.Spelling.Split(' ')) { position += syllable.Length; boundaries.Add(position); }
                    _boundaries[word.Id] = boundaries.ToArray();
                }
            }
            internal bool Fits(LexiconWord word, PinyinSpelling.Input input, int start, int end)
            {
                foreach (int boundary in input.Boundaries)
                    if (boundary > start && boundary < end && Array.IndexOf(_boundaries[word.Id], boundary - start) < 0) return false;
                return true;
            }
        }
        private sealed class Path
        {
            internal readonly string Text;
            internal readonly LexiconWord[] Words;
            internal readonly double Score;
            internal Path(string text, LexiconWord[] words, double score) { Text = text; Words = words; Score = score; }
        }
        private sealed class Engine : TokenLanguageEngine
        {
            private const int BeamWidth = 8;
            internal Engine() : base("zh.full-pinyin") { }
            protected override bool UsesCandidates => true;
            protected override bool IsCompositionCharacter(char value) => PinyinSpelling.IsInput(value);
            public override Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken) => Task.Run(() => Query(query, cancellationToken), cancellationToken);
            private static CandidatePage Query(CandidateQuery query, CancellationToken cancellationToken)
            {
                var input = new PinyinSpelling.Input(query.Composition.RawText);
                if (input.Key.Length == 0) return new CandidatePage(query, Array.Empty<Candidate>());
                var index = query.Data.Index as Index ?? new Index(query.Data.Words);
                var candidates = new List<Candidate>();
                var paths = new List<Path>?[input.Key.Length + 1];
                paths[0] = new List<Path> { new Path("", Array.Empty<LexiconWord>(), 0) };

                for (int start = 0; start < input.Key.Length; start++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (paths[start] == null) continue;
                    for (int length = 1; length <= Math.Min(index.Words.MaxKeyLength, input.Key.Length - start); length++)
                    {
                        int end = start + length;
                        var matches = index.Words.Exact(input.Key.Substring(start, length)).Where(word => index.Fits(word, input, start, end)).OrderByDescending(word => Score(word, query, index)).Take(BeamWidth).ToArray();
                        foreach (var word in matches)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (start == 0) candidates.Add(ToCandidate(word.Text, input.Consumed(end), new[] { word }, Score(word, query, index)));
                            var next = paths[end] ?? (paths[end] = new List<Path>());
                            foreach (var path in paths[start]!) next.Add(new Path(path.Text + word.Text, path.Words.Concat(new[] { word }).ToArray(), path.Score + Score(word, query, index)));
                            paths[end] = next.GroupBy(path => path.Text, StringComparer.Ordinal).Select(group => group.OrderByDescending(path => path.Score).First()).OrderByDescending(path => path.Score).Take(BeamWidth).ToList();
                        }
                    }
                }
                if (paths[input.Key.Length] != null)
                    foreach (var path in paths[input.Key.Length]!) candidates.Add(ToCandidate(path.Text, input.Raw.Length, path.Words, path.Score));
                foreach (var word in index.Words.Prefix(input.Key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (index.Fits(word, input, 0, input.Key.Length)) candidates.Add(ToCandidate(word.Text, input.Raw.Length, new[] { word }, Score(word, query, index) - (PinyinSpelling.Flatten(word.Spelling).Length > input.Key.Length ? 16 : 0)));
                }
                var ordered = candidates.GroupBy(candidate => candidate.Text + "\t" + candidate.ConsumedLength, StringComparer.Ordinal).Select(group => group.OrderByDescending(candidate => candidate.Score).First()).OrderByDescending(candidate => candidate.ConsumedLength).ThenByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Text, StringComparer.Ordinal).ToArray();
                return new CandidatePage(query, ordered.Skip(query.Offset).Take(query.PageSize).Select((candidate, rank) => new Candidate((query.Offset + rank).ToString(CultureInfo.InvariantCulture), candidate.Text, candidate.ConsumedLength, candidate.Words, candidate.Score)), query.Offset + query.PageSize < ordered.Length);
            }
            private static Candidate ToCandidate(string text, int consumed, IEnumerable<LexiconWord> words, double score) => new Candidate("", text, consumed, words.Select(word => new WordUsage(word.Id)), score);
            private static double Score(LexiconWord word, CandidateQuery query, Index index)
            {
                query.Frequencies.TryGetValue(word.Id, out long count);
                return Math.Log(1 + word.Weight) - index.Words.LogTotalWeight + 4 * Math.Log(1d + count);
            }
        }
    }
}

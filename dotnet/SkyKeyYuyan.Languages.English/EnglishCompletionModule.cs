using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Internal;

namespace SkyKeyYuyan.Languages.English
{
    /// <summary>
    /// 根据离线词表补全当前英语词首。
    /// </summary>
    public sealed class EnglishCompletionModule : LexiconModule, IKeyboardLayoutSource
    {
        /// <summary>
        /// 从 Provider 资源或显式词条准备补全词典。
        /// </summary>
        public EnglishCompletionModule(IEnumerable<LexiconWord>? words = null, string resourceId = "english/lexicon.bytes") : base(resourceId, words) { }
        /// <inheritdoc>
        /// </inheritdoc>
        public override string LanguageId => "en";
        /// <inheritdoc>
        /// </inheritdoc>
        public override string EngineId => "en.completion";
        /// <inheritdoc>
        /// </inheritdoc>
        public override string DisplayName => "English";
        /// <inheritdoc>
        /// </inheritdoc>
        public override LanguageCapabilities Capabilities => LanguageCapabilities.Candidates | LanguageCapabilities.CompletionSwitch;
        /// <inheritdoc>
        /// </inheritdoc>
        public KeyboardLayout LoadKeyboard(IResourceProvider resources) => KeyboardLayout.Parse(resources.Read("english/keyboard.bytes"), LanguageId);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string NormalizeSpelling(string spelling)
        {
            string normalized = spelling.Trim().ToLowerInvariant();
            if (normalized.Length == 0 || normalized.Any(value => value < 'a' || value > 'z')) throw new ArgumentException("英语匹配拼写须使用拉丁字母");
            return normalized;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override object BuildIndex(IReadOnlyList<LexiconWord> words) => new SpellingIndex(words, spelling => spelling);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override ILanguageEngine CreateLanguageEngine() => new Engine();

        private sealed class Engine : TokenLanguageEngine
        {
            private readonly struct RankedWord
            {
                internal readonly LexiconWord Word;
                internal readonly double Score;
                internal RankedWord(LexiconWord word, double score) { Word = word; Score = score; }
            }
            internal Engine() : base("en.completion") { }
            protected override bool UsesCandidates => CompletionEnabled;
            protected override bool DirectInput => !CompletionEnabled;
            protected override bool IsCompositionCharacter(char value) => value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z';
            public override Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken) => Task.Run(() => Query(query, cancellationToken), cancellationToken);
            private static CandidatePage Query(CandidateQuery query, CancellationToken cancellationToken)
            {
                string raw = query.Composition.RawText;
                if (raw.Length == 0) return new CandidatePage(query, Array.Empty<Candidate>());
                var index = query.Data.Index as SpellingIndex ?? new SpellingIndex(query.Data.Words, word => word.ToLowerInvariant());
                var matches = new RankedWindow<RankedWord>((long)query.Offset + query.PageSize + 1, (left, right) =>
                {
                    int score = right.Score.CompareTo(left.Score);
                    return score != 0 ? score : StringComparer.Ordinal.Compare(left.Word.Text, right.Word.Text);
                });
                foreach (var word in index.Prefix(raw.ToLowerInvariant()))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    query.Frequencies.TryGetValue(word.Id, out long count);
                    double score = Math.Log(1 + word.Weight) + 4 * Math.Log(1d + count) - 0.02 * word.Text.Length;
                    matches.Add(new RankedWord(word, score));
                }
                var candidates = matches.GetPage(query.Offset, query.PageSize).Select((pair, rank) => new Candidate((query.Offset + rank).ToString(CultureInfo.InvariantCulture), ApplyCase(pair.Word.Text, raw), raw.Length, new[] { new WordUsage(pair.Word.Id) }, pair.Score));
                return new CandidatePage(query, candidates, (long)query.Offset + query.PageSize < matches.Seen);
            }
            private static string ApplyCase(string text, string raw)
            {
                if (raw.Length == 0) return text;
                if (raw.Length > 1 && raw.All(char.IsUpper)) return text.ToUpperInvariant();
                if (char.IsUpper(raw[0]) && raw.Skip(1).All(char.IsLower)) return char.ToUpperInvariant(text[0]) + text.Substring(1);
                if (raw.Any(char.IsUpper) && text.Length >= raw.Length) return raw + text.Substring(raw.Length);
                return text;
            }
        }
    }
}

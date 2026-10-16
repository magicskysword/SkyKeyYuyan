using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.English
{
    /// <summary>
    /// 根据离线词表补全当前英语词首。
    /// </summary>
    public sealed class EnglishCompletionModule : LexiconModule
    {
        /// <summary>
        /// 从 Provider 资源或显式词条准备补全词典。
        /// </summary>
        public EnglishCompletionModule(IEnumerable<LexiconWord>? words = null, string resourceId = "english/lexicon.bytes") : base(resourceId, words) { }
        /// <inheritdoc />
        public override string LanguageId => "en";
        /// <inheritdoc />
        public override string EngineId => "en.completion";
        /// <inheritdoc />
        public override string DisplayName => "English";
        /// <inheritdoc />
        public override LanguageCapabilities Capabilities => LanguageCapabilities.Candidates | LanguageCapabilities.CompletionSwitch;
        /// <inheritdoc />
        protected override string NormalizeSpelling(string spelling)
        {
            string normalized = spelling.Trim().ToLowerInvariant();
            if (normalized.Length == 0 || normalized.Any(value => value < 'a' || value > 'z')) throw new ArgumentException("英语匹配拼写须使用拉丁字母");
            return normalized;
        }
        /// <inheritdoc />
        protected override object BuildIndex(IReadOnlyList<LexiconWord> words) => new SpellingIndex(words, spelling => spelling);
        /// <inheritdoc />
        protected override ILanguageEngine CreateLanguageEngine() => new Engine();

        private sealed class Engine : TokenLanguageEngine
        {
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
                var matches = new List<KeyValuePair<LexiconWord, double>>();
                foreach (var word in index.Prefix(raw.ToLowerInvariant()))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    query.Frequencies.TryGetValue(word.Id, out long count);
                    double score = Math.Log(1 + word.Weight) + 4 * Math.Log(1d + count) - 0.02 * word.Text.Length;
                    matches.Add(new KeyValuePair<LexiconWord, double>(word, score));
                }
                var ordered = matches.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Text, StringComparer.Ordinal).ToArray();
                var candidates = ordered.Skip(query.Offset).Take(query.PageSize).Select((pair, rank) => new Candidate((query.Offset + rank).ToString(CultureInfo.InvariantCulture), ApplyCase(pair.Key.Text, raw), raw.Length, new[] { new WordUsage(pair.Key.Id) }, pair.Value));
                return new CandidatePage(query, candidates, query.Offset + query.PageSize < ordered.Length);
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

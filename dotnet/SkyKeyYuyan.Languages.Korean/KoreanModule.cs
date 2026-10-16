using System;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Korean
{
    /// <summary>
    /// 现代韩语两套式输入模块。
    /// </summary>
    public sealed class KoreanModule : ILanguageModule
    {
        private sealed class Engine : TokenLanguageEngine
        {
            internal Engine() : base("ko.two-set") { }
            protected override string Render(string raw) => HangulComposer.Compose(raw);
            protected override bool IsCompositionCharacter(char value) => HangulComposer.IsInput(value);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public string LanguageId => "ko";
        /// <inheritdoc>
        /// </inheritdoc>
        public string EngineId => "ko.two-set";
        /// <inheritdoc>
        /// </inheritdoc>
        public string DisplayName => "한국어";
        /// <inheritdoc>
        /// </inheritdoc>
        public LanguageCapabilities Capabilities => LanguageCapabilities.None;
        /// <inheritdoc>
        /// </inheritdoc>
        public event Action DataChanged { add { } remove { } }
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult Initialize(IResourceProvider resources) => new ServiceResult(true, "language");
        /// <inheritdoc>
        /// </inheritdoc>
        public Task<ServiceResult> InitializeAsync(IResourceProvider resources, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Initialize(resources)); }
        /// <inheritdoc>
        /// </inheritdoc>
        public ILanguageEngine CreateEngine() => new Engine();
        /// <inheritdoc>
        /// </inheritdoc>
        public LanguageDataSnapshot CaptureData() => new LanguageDataSnapshot(0);
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult UpsertWord(LexiconWord word) => new ServiceResult(false, "dictionary", "韩语组合使用字母规则");
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult RemoveWord(string wordId) => new ServiceResult(false, "dictionary", "韩语组合使用字母规则");
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { }
    }
}

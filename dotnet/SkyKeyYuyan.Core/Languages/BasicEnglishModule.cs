using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 核心内置的英语直接输入模块。
    /// </summary>
    public sealed class BasicEnglishModule : ILanguageModule
    {
        private sealed class Engine : HistoryLanguageEngine
        {
            internal Engine() : base("en.direct") { }
            protected override bool DirectInput => true;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public string LanguageId => "en";
        /// <inheritdoc>
        /// </inheritdoc>
        public string EngineId => "en.direct";
        /// <inheritdoc>
        /// </inheritdoc>
        public string DisplayName => "English";
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
        public ServiceResult UpsertWord(LexiconWord word) => new ServiceResult(false, "dictionary", "直接输入引擎没有词典");
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult RemoveWord(string wordId) => new ServiceResult(false, "dictionary", "直接输入引擎没有词典");
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { }
    }
}

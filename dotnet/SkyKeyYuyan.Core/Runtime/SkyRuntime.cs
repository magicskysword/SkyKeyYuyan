using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 初始化时固定的宿主服务与语言实现配置。
    /// </summary>
    public sealed class RuntimeOptions
    {
        /// <summary>
        /// 会话工作及事件的串行执行上下文。
        /// </summary>
        public ISkyExecutionContext ExecutionContext { get; set; } = new SerialExecutionContext();
        /// <summary>
        /// 由宿主持有的资源 Provider。
        /// </summary>
        public IResourceProvider Resources { get; set; } = new DirectoryResourceProvider(Path.Combine(AppContext.BaseDirectory, "sky-data"));
        /// <summary>
        /// 每种语言使用的显式引擎标识。
        /// </summary>
        public IReadOnlyDictionary<string, string> LanguageEngines { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// 按语言替换默认键盘布局。
        /// </summary>
        public IReadOnlyDictionary<string, KeyboardLayout> Keyboards { get; set; } = new Dictionary<string, KeyboardLayout>();
        /// <summary>
        /// 初始化成功后是否取代进程默认实例。
        /// </summary>
        public bool ReplaceDefault { get; set; } = true;
        /// <summary>
        /// 省略会话用户时使用的标识。
        /// </summary>
        public string DefaultUserId { get; set; } = "default";
        /// <summary>
        /// 首次配置进程共享词频的存档 Provider。
        /// </summary>
        public IFrequencyStore? FrequencyStore { get; set; }
        /// <summary>
        /// 除默认用户外，在初始化时加载词频的用户。
        /// </summary>
        public IReadOnlyList<string> PreloadUsers { get; set; } = Array.Empty<string>();
        /// <summary>
        /// 是否在会话及运行时结束时自动保存词频。
        /// </summary>
        public bool AutoSaveFrequencies { get; set; } = true;
        /// <summary>
        /// 接收带阶段与操作归属的诊断。
        /// </summary>
        public Action<SkyDiagnostic>? DiagnosticSink { get; set; }
    }
    /// <summary>
    /// 带关联身份的运行诊断。
    /// </summary>
    public sealed class SkyDiagnostic
    {
        /// <summary>
        /// 创建可定位的诊断。
        /// </summary>
        public SkyDiagnostic(string stage, string reason, string engineId = "", Guid? sessionId = null, OperationId? operation = null, bool recovered = false)
        { Stage = stage; Reason = reason; EngineId = engineId; SessionId = sessionId; Operation = operation; Recovered = recovered; }
        /// <summary>
        /// 失败或恢复阶段。
        /// </summary>
        public string Stage { get; }
        /// <summary>
        /// 可读原因。
        /// </summary>
        public string Reason { get; }
        /// <summary>
        /// 关联实现。
        /// </summary>
        public string EngineId { get; }
        /// <summary>
        /// 关联会话。
        /// </summary>
        public Guid? SessionId { get; }
        /// <summary>
        /// 关联操作。
        /// </summary>
        public OperationId? Operation { get; }
        /// <summary>
        /// 是否已恢复。
        /// </summary>
        public bool Recovered { get; }
    }
    /// <summary>
    /// 首次或单语言初始化的结果。
    /// </summary>
    public sealed class InitializationResult
    {
        internal InitializationResult(ServiceResult result, IEnumerable<string> readyEngines, IEnumerable<FrequencyStoreResult>? preloadResults = null) { Result = result; ReadyEngines = Array.AsReadOnly(readyEngines.ToArray()); PreloadResults = Array.AsReadOnly((preloadResults ?? Enumerable.Empty<FrequencyStoreResult>()).ToArray()); }
        /// <summary>
        /// 成功、失败或取消及对应阶段。
        /// </summary>
        public ServiceResult Result { get; }
        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded => Result.Succeeded;
        /// <summary>
        /// 当时已就绪的引擎。
        /// </summary>
        public IReadOnlyList<string> ReadyEngines { get; }
        /// <summary>
        /// 不影响语言初始化成功状态的词频预加载结果。
        /// </summary>
        public IReadOnlyList<FrequencyStoreResult> PreloadResults { get; }
    }
    /// <summary>
    /// 打开输入的确定结果。
    /// </summary>
    public sealed class SessionOpenResult
    {
        internal SessionOpenResult(ISkySession? session, string? reason = null) { Session = session; Reason = reason; }
        /// <summary>
        /// 成功打开的会话。
        /// </summary>
        public ISkySession? Session { get; }
        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded => Session != null;
        /// <summary>
        /// 未打开的原因。
        /// </summary>
        public string? Reason { get; }
    }
    /// <summary>
    /// 管理语言资源和独立输入会话。
    /// </summary>
    public interface ISkyRuntime : IDisposable
    {
        /// <summary>
        /// 添加显式语言模块与工厂。
        /// </summary>
        ServiceResult RegisterLanguage(ILanguageModule module);
        /// <summary>
        /// 同步准备所有注册模块。
        /// </summary>
        InitializationResult Initialize(RuntimeOptions options);
        /// <summary>
        /// 异步准备所有注册模块。
        /// </summary>
        Task<InitializationResult> InitializeAsync(RuntimeOptions options, CancellationToken cancellationToken);
        /// <summary>
        /// 同步准备运行期间添加的模块。
        /// </summary>
        InitializationResult InitializeLanguage(string engineId);
        /// <summary>
        /// 异步准备运行期间添加的模块。
        /// </summary>
        Task<InitializationResult> InitializeLanguageAsync(string engineId, CancellationToken cancellationToken);
        /// <summary>
        /// 在指定文本目标上打开会话。
        /// </summary>
        SessionOpenResult OpenSession(SessionOptions options, ITextTarget target);
        /// <summary>
        /// 显式保存进程内待保存的词频。
        /// </summary>
        IReadOnlyList<FrequencyStoreResult> SaveFrequencies();
        /// <summary>
        /// 等待所有已安排的词频读写完成。
        /// </summary>
        Task<IReadOnlyList<FrequencyStoreResult>> SaveFrequenciesAsync(CancellationToken cancellationToken);
    }
    /// <summary>
    /// 可独立使用的输入运行时，默认实例仅在成功初始化后替换。
    /// </summary>
    public sealed class SkyRuntime : ISkyRuntime
    {
        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
        private static readonly object GlobalGate = new object();
        private static readonly Dictionary<object, Guid> TargetOwners = new Dictionary<object, Guid>(new IdentityComparer());
        private static readonly Dictionary<string, string> RecentLanguages = new Dictionary<string, string>(StringComparer.Ordinal);
        private static SkyRuntime? _default;
        private readonly object _gate = new object();
        private readonly Dictionary<string, ILanguageModule> _modules = new Dictionary<string, ILanguageModule>(StringComparer.Ordinal);
        private readonly Dictionary<string, ILanguageModule> _selected = new Dictionary<string, ILanguageModule>(StringComparer.Ordinal);
        private readonly Dictionary<string, KeyboardLayout> _keyboards = new Dictionary<string, KeyboardLayout>(StringComparer.Ordinal);
        private readonly HashSet<string> _ready = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<SkySession> _sessions = new HashSet<SkySession>();
        private readonly HashSet<ILanguageModule> _releasedModules = new HashSet<ILanguageModule>();
        private bool _initializing, _initialized, _retired, _disposed, _resourcesReleased;
        private int _workCount;
        private RuntimeOptions _options = new RuntimeOptions();
        private FrequencyService? _frequency;
        private FrequencyStoreResult[] _preloadResults = Array.Empty<FrequencyStoreResult>();
        private bool _frequencySubscribed;

        /// <summary>
        /// 最近一次成功发布的默认运行时。
        /// </summary>
        public static ISkyRuntime? Default { get { lock (GlobalGate) return _default; } }
        internal ISkyExecutionContext Context => _options.ExecutionContext;
        internal string DefaultUserId => _options.DefaultUserId;
        internal FrequencyService Frequency => _frequency ?? FrequencyService.Shared;

        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult RegisterLanguage(ILanguageModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (_gate)
            {
                if (_disposed || _retired || _initializing) return new ServiceResult(false, "registration", "运行时当前不能注册模块");
                if (_modules.ContainsKey(module.EngineId)) return new ServiceResult(false, "registration", "引擎标识已注册");
                _modules.Add(module.EngineId, module);
                return new ServiceResult(true, "registration");
            }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public InitializationResult Initialize(RuntimeOptions options)
        {
            var begin = BeginInitialization(options, out var modules);
            if (!begin.Succeeded) return Result(begin);
            var acquired = new List<ILanguageModule>();
            ServiceResult result = new ServiceResult(true, "initialize");
            foreach (var module in modules)
            {
                acquired.Add(module);
                lock (_gate) _releasedModules.Remove(module);
                try { result = module.Initialize(_options.Resources); }
                catch (Exception ex) { result = new ServiceResult(false, "language:" + module.EngineId, ex.Message); }
                if (!result.Succeeded) break;
            }
            if (result.Succeeded) _preloadResults = Preload(modules.Select(module => module.LanguageId));
            return CompleteInitialization(result, modules, acquired);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public async Task<InitializationResult> InitializeAsync(RuntimeOptions options, CancellationToken cancellationToken)
        {
            var begin = BeginInitialization(options, out var modules);
            if (!begin.Succeeded) return Result(begin);
            var acquired = new List<ILanguageModule>();
            ServiceResult result = new ServiceResult(true, "initialize");
            try
            {
                foreach (var module in modules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    acquired.Add(module);
                    lock (_gate) _releasedModules.Remove(module);
                    result = await module.InitializeAsync(_options.Resources, cancellationToken).ConfigureAwait(false);
                    if (!result.Succeeded) break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Succeeded) _preloadResults = await PreloadAsync(modules.Select(module => module.LanguageId), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { result = new ServiceResult(false, "initialize", "初始化已取消", true); }
            catch (Exception ex) { result = new ServiceResult(false, "initialize", ex.Message); }
            return CompleteInitialization(result, modules, acquired);
        }
        private ServiceResult BeginInitialization(RuntimeOptions options, out ILanguageModule[] modules)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            lock (_gate)
            {
                modules = Array.Empty<ILanguageModule>();
                if (_disposed || _retired || _initialized || _initializing) return new ServiceResult(false, "initialize", "运行时当前不能初始化");
                if (options.ExecutionContext == null || options.Resources == null || string.IsNullOrWhiteSpace(options.DefaultUserId)) return new ServiceResult(false, "configuration", "执行上下文、资源和默认用户不能为空");
                foreach (var pair in options.LanguageEngines)
                    if (!_modules.TryGetValue(pair.Value, out var module) || module.LanguageId != pair.Key) return new ServiceResult(false, "configuration", "语言实现配置未注册");
                foreach (var group in _modules.Values.GroupBy(module => module.LanguageId))
                    if (group.Count() > 1 && !options.LanguageEngines.ContainsKey(group.Key)) return new ServiceResult(false, "configuration", "同语言多个实现需要显式选择");
                if (options.PreloadUsers.Any(string.IsNullOrWhiteSpace)) return new ServiceResult(false, "configuration", "预加载用户标识不能为空");
                if (options.Keyboards.Any(pair => pair.Value == null || pair.Key != pair.Value.LanguageId)) return new ServiceResult(false, "configuration", "替换布局的语言身份不匹配");
                if (options.FrequencyStore != null) FrequencyService.ConfigureStore(options.FrequencyStore);
                _frequency = FrequencyService.Shared;
                _options = new RuntimeOptions { ExecutionContext = options.ExecutionContext, Resources = options.Resources, DefaultUserId = options.DefaultUserId, ReplaceDefault = options.ReplaceDefault, DiagnosticSink = options.DiagnosticSink, LanguageEngines = options.LanguageEngines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), FrequencyStore = options.FrequencyStore, PreloadUsers = options.PreloadUsers.ToArray(), AutoSaveFrequencies = options.AutoSaveFrequencies };
                _preloadResults = Array.Empty<FrequencyStoreResult>();
                _keyboards.Clear();
                foreach (var keyboard in options.Keyboards) _keyboards.Add(keyboard.Key, keyboard.Value);
                _initializing = true;
                modules = _modules.Values.ToArray();
                return new ServiceResult(true, "initialize");
            }
        }
        private InitializationResult CompleteInitialization(ServiceResult result, ILanguageModule[] modules, List<ILanguageModule> acquired)
        {
            bool publish = false;
            lock (_gate)
            {
                if (_disposed) result = new ServiceResult(false, "initialize", "运行时已释放");
                if (result.Succeeded)
                {
                    foreach (var module in modules)
                    {
                        _ready.Add(module.EngineId);
                        if (!_options.LanguageEngines.TryGetValue(module.LanguageId, out var engineId) || engineId == module.EngineId) _selected[module.LanguageId] = module;
                    }
                    _initialized = true; publish = _options.ReplaceDefault;
                    if (!_frequencySubscribed) { Frequency.Diagnostic += Report; _frequencySubscribed = true; }
                }
            }
            if (!result.Succeeded) foreach (var module in acquired.AsEnumerable().Reverse()) DisposeModule(module);
            lock (_gate) _initializing = false;
            if (publish)
            {
                SkyRuntime? previous;
                lock (_gate)
                {
                    if (_disposed) return Result(new ServiceResult(false, "initialize", "运行时已释放"));
                    lock (GlobalGate) { previous = _default; _default = this; }
                }
                if (previous != null && !ReferenceEquals(previous, this)) previous.Retire();
            }
            TryReleaseResources();
            return Result(result);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public InitializationResult InitializeLanguage(string engineId)
        {
            var module = BeginLanguageInitialization(engineId, out var error);
            if (module == null) return Result(error!);
            ServiceResult result;
            try { result = module.Initialize(_options.Resources); }
            catch (Exception ex) { result = new ServiceResult(false, "language:" + engineId, ex.Message); }
            if (result.Succeeded) _preloadResults = Preload(new[] { module.LanguageId });
            return CompleteLanguageInitialization(module, result);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public async Task<InitializationResult> InitializeLanguageAsync(string engineId, CancellationToken cancellationToken)
        {
            var module = BeginLanguageInitialization(engineId, out var error);
            if (module == null) return Result(error!);
            ServiceResult result;
            try
            {
                result = await module.InitializeAsync(_options.Resources, cancellationToken).ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested();
                if (result.Succeeded) _preloadResults = await PreloadAsync(new[] { module.LanguageId }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { result = new ServiceResult(false, "language:" + engineId, "初始化已取消", true); }
            catch (Exception ex) { result = new ServiceResult(false, "language:" + engineId, ex.Message); }
            return CompleteLanguageInitialization(module, result);
        }
        private ILanguageModule? BeginLanguageInitialization(string engineId, out ServiceResult? error)
        {
            lock (_gate)
            {
                error = null;
                if (!_initialized || _disposed || _retired || _initializing || !_modules.TryGetValue(engineId, out var module) || _ready.Contains(engineId)) { error = new ServiceResult(false, "language", "模块不存在、已就绪或运行时当前不可准备模块"); return null; }
                _initializing = true;
                _releasedModules.Remove(module);
                return module;
            }
        }
        private InitializationResult CompleteLanguageInitialization(ILanguageModule module, ServiceResult result)
        {
            lock (_gate)
            {
                if (_disposed) result = new ServiceResult(false, "language", "运行时已释放");
                if (result.Succeeded) { _ready.Add(module.EngineId); if (!_selected.ContainsKey(module.LanguageId)) _selected.Add(module.LanguageId, module); }
            }
            if (!result.Succeeded) DisposeModule(module);
            lock (_gate) _initializing = false;
            TryReleaseResources();
            return Result(result);
        }
        private InitializationResult Result(ServiceResult result) { lock (_gate) return new InitializationResult(result, _ready, _preloadResults); }
        private FrequencyStoreResult[] Preload(IEnumerable<string> languageIds)
        {
            return (from user in new[] { _options.DefaultUserId }.Concat(_options.PreloadUsers).Distinct(StringComparer.Ordinal)
                    from language in languageIds.Distinct(StringComparer.Ordinal)
                    select Frequency.Load(user, language)).ToArray();
        }
        private async Task<FrequencyStoreResult[]> PreloadAsync(IEnumerable<string> languageIds, CancellationToken cancellationToken)
        {
            var requests = (from user in new[] { _options.DefaultUserId }.Concat(_options.PreloadUsers).Distinct(StringComparer.Ordinal)
                            from language in languageIds.Distinct(StringComparer.Ordinal)
                            select Frequency.LoadAsync(user, language)).ToArray();
            var all = Task.WhenAll(requests);
            if (!cancellationToken.CanBeCanceled) return await all.ConfigureAwait(false);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(all, cancelled.Task).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await all.ConfigureAwait(false);
            }
        }

        /// <inheritdoc>
        /// </inheritdoc>
        public SessionOpenResult OpenSession(SessionOptions options, ITextTarget target)
        {
            if (options == null || target == null) throw new ArgumentNullException(options == null ? nameof(options) : nameof(target));
            Guid sessionId = Guid.NewGuid();
            lock (_gate)
            {
                if (!_initialized || _disposed || _retired) return new SessionOpenResult(null, "运行时未就绪或已经结束");
                var languages = (options.Languages ?? _selected.Keys.ToArray()).Distinct(StringComparer.Ordinal).ToArray();
                if (languages.Length == 0 || languages.Any(language => !_selected.ContainsKey(language))) return new SessionOpenResult(null, "会话语言尚未就绪");
                if (string.IsNullOrWhiteSpace(options.PlayerId)) return new SessionOpenResult(null, "玩家标识不能为空");
                string initial = options.InitialLanguage ?? RecentLanguage(options.PlayerId) ?? languages[0];
                if (!languages.Contains(initial, StringComparer.Ordinal)) initial = languages[0];
                object identity = target.Identity ?? throw new ArgumentException("目标身份不能为空", nameof(target));
                if (!AcquireTarget(identity, sessionId)) return new SessionOpenResult(null, "文本目标正在使用");
                object? draftIdentity = null;
                try
                {
                    var original = target.ReadSnapshot();
                    ITextTarget editing = target;
                    if (options.EditingMode == EditingMode.Draft)
                    {
                        editing = options.DraftTargetFactory == null ? new MemoryTextTarget(original.Text, original.Anchor, original.Caret)
                            : options.DraftTargetFactory(original) ?? throw new InvalidOperationException("草稿工厂没有返回目标");
                        object identityToAcquire = editing.Identity ?? throw new ArgumentException("草稿目标身份不能为空");
                        if (!AcquireTarget(identityToAcquire, sessionId)) throw new InvalidOperationException("草稿文本目标正在使用");
                        draftIdentity = identityToAcquire;
                        var draft = editing.ReadSnapshot();
                        if (draft.Text != original.Text || draft.Anchor != original.Anchor || draft.Caret != original.Caret) throw new InvalidOperationException("草稿初始正文和选区与原文不一致");
                        var current = target.ReadSnapshot();
                        if (current.Version != original.Version) throw new InvalidOperationException("创建草稿期间原目标已经改变");
                    }
                    var session = new SkySession(this, sessionId, target, editing, options, languages.ToDictionary(language => language, language => _selected[language], StringComparer.Ordinal), initial);
                    _sessions.Add(session);
                    session.PrepareUser();
                    RememberLanguage(options.PlayerId, initial);
                    return new SessionOpenResult(session);
                }
                catch (Exception ex)
                {
                    ReleaseTarget(identity, sessionId);
                    if (draftIdentity != null) ReleaseTarget(draftIdentity, sessionId);
                    return new SessionOpenResult(null, ex.Message);
                }
            }
        }
        private static string? RecentLanguage(string playerId) { lock (GlobalGate) return RecentLanguages.TryGetValue(playerId, out var language) ? language : null; }
        internal KeyboardLayout GetKeyboard(ILanguageModule module)
        {
            lock (_gate)
            {
                if (_keyboards.TryGetValue(module.LanguageId, out var keyboard)) return keyboard;
                if (_resourcesReleased) throw new ObjectDisposedException(nameof(SkyRuntime));
                if (!(module is IKeyboardLayoutSource source)) throw new NotSupportedException("当前语言没有默认键盘布局");
                keyboard = source.LoadKeyboard(_options.Resources);
                if (keyboard.LanguageId != module.LanguageId) throw new InvalidDataException("键盘布局与模块语言不一致");
                _keyboards.Add(module.LanguageId, keyboard); return keyboard;
            }
        }
        private static bool AcquireTarget(object identity, Guid sessionId)
        {
            lock (GlobalGate)
            {
                if (TargetOwners.ContainsKey(identity)) return false;
                TargetOwners.Add(identity, sessionId); return true;
            }
        }
        internal static void ReleaseTarget(object identity, Guid sessionId)
        {
            lock (GlobalGate) if (TargetOwners.TryGetValue(identity, out var owner) && owner == sessionId) TargetOwners.Remove(identity);
        }
        internal static void RememberLanguage(string playerId, string languageId) { lock (GlobalGate) RecentLanguages[playerId] = languageId; }
        internal void SessionEnded(SkySession session, object identity, Guid sessionId)
        {
            ReleaseTarget(identity, sessionId);
            lock (_gate) _sessions.Remove(session);
            TryReleaseResources();
        }
        internal void RetainWork() { lock (_gate) _workCount++; }
        internal void ReleaseWork() { lock (_gate) _workCount--; TryReleaseResources(); }
        private void Retire() { lock (_gate) _retired = true; TryReleaseResources(); }
        private void TryReleaseResources()
        {
            ILanguageModule[] modules;
            lock (_gate)
            {
                if ((!_disposed && !_retired) || _resourcesReleased || _initializing || _sessions.Count != 0 || _workCount != 0) return;
                _resourcesReleased = true; modules = _modules.Values.Reverse().ToArray();
            }
            if (_options.AutoSaveFrequencies && _frequency != null) _ = SaveOnReleaseAsync();
            foreach (var module in modules) DisposeModule(module);
            if (_frequencySubscribed) { Frequency.Diagnostic -= Report; _frequencySubscribed = false; }
        }
        private void DisposeModule(ILanguageModule module)
        {
            lock (_gate) if (!_releasedModules.Add(module)) return;
            try { module.Dispose(); } catch (Exception ex) { Report(new SkyDiagnostic("dispose", ex.Message)); }
        }
        private async Task SaveOnReleaseAsync()
        {
            var results = await Frequency.SaveAllAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var result in results)
                if (!result.Result.Succeeded) Report(new SkyDiagnostic("frequency-save", result.Result.Reason ?? "运行时结束后的词频保存失败"));
        }
        internal void Report(SkyDiagnostic diagnostic)
        {
            _options.ExecutionContext.Post(() =>
            {
                try { _options.DiagnosticSink?.Invoke(diagnostic); } catch { /* 诊断接收者不参与输入状态转换。 */ }
            });
        }
        internal IReadOnlyList<FrequencyStoreResult> SaveUser(string userId, IEnumerable<string> languages) => _options.AutoSaveFrequencies ? Array.AsReadOnly(languages.Select(language => Frequency.Save(userId, language)).ToArray()) : Array.Empty<FrequencyStoreResult>();
        /// <inheritdoc>
        /// </inheritdoc>
        public IReadOnlyList<FrequencyStoreResult> SaveFrequencies() => Frequency.SaveAll();
        /// <inheritdoc>
        /// </inheritdoc>
        public Task<IReadOnlyList<FrequencyStoreResult>> SaveFrequenciesAsync(CancellationToken cancellationToken) => Frequency.SaveAllAsync(cancellationToken);
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            SkySession[] sessions;
            lock (_gate) { if (_disposed) return; _disposed = true; sessions = _sessions.ToArray(); }
            lock (GlobalGate) if (ReferenceEquals(_default, this)) _default = null;
            foreach (var session in sessions) session.EndFromRuntime();
            TryReleaseResources();
        }
    }
}

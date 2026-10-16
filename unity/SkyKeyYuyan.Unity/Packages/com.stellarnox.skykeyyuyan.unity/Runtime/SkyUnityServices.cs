using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 为 Unity 运行时提供共享主线程队列、资源及默认存档。
    /// </summary>
    public sealed class SkyUnityServices
    {
        private static SkyUnityServices _shared;
        private readonly PlayerPrefsFrequencyStore _store;
        internal bool RuntimeConfigured { get; private set; }

        private SkyUnityServices()
        {
            Context = new PumpExecutionContext();
            Resources = new UnityResourceProvider(Context);
            _store = new PlayerPrefsFrequencyStore(Context);
            var runner = new GameObject("SkyKeyYuyan Services") { hideFlags = HideFlags.HideAndDontSave };
            runner.AddComponent<SkyUnityDispatcher>();
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(runner);
        }
        /// <summary>
        /// 在 Unity 主线程首次取得进程服务。
        /// </summary>
        public static SkyUnityServices Shared => _shared ?? (_shared = new SkyUnityServices());
        /// <summary>
        /// Unity 主线程串行执行上下文。
        /// </summary>
        public PumpExecutionContext Context { get; }
        /// <summary>
        /// 从语言包 Resources 目录读取资源。
        /// </summary>
        public UnityResourceProvider Resources { get; }
        /// <summary>
        /// 创建交给独立运行时初始化的 Unity 配置。
        /// </summary>
        public RuntimeOptions CreateOptions()
        {
            FrequencyService.ConfigureStore(_store);
            RuntimeConfigured = true;
            return new RuntimeOptions { ExecutionContext = Context, Resources = Resources, FrequencyStore = _store, DiagnosticSink = diagnostic => Debug.LogWarning("SKY " + diagnostic.Stage + ": " + diagnostic.Reason) };
        }
        internal static void Pump() { _shared?.Context.Drain(); }
        internal static void SaveBeforeExit()
        {
            if (_shared == null || !_shared.RuntimeConfigured) return;
            _shared.Context.Drain(); FrequencyService.Shared.SaveAll(); _shared.Context.Drain();
        }
    }

    /// <summary>
    /// 从已安装语言包读取 Unity 文本资源。
    /// </summary>
    public sealed class UnityResourceProvider : IResourceProvider, IDisposable
    {
        private readonly ISkyExecutionContext _context;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, TextAsset> _assets = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
        private bool _disposed;
        /// <summary>
        /// 设置执行 Unity 资源 API 的主线程上下文。
        /// </summary>
        public UnityResourceProvider(ISkyExecutionContext context) { _context = context ?? throw new ArgumentNullException(nameof(context)); }
        /// <inheritdoc>
        /// </inheritdoc>
        public byte[] Read(string resourceId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityResourceProvider));
            if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Unity 资源同步读取需要主线程");
            if (string.IsNullOrWhiteSpace(resourceId) || resourceId.Contains("..") || resourceId.StartsWith("/", StringComparison.Ordinal) || resourceId.Contains("\\")) throw new ArgumentException("语言资源标识无效", nameof(resourceId));
            if (!_assets.TryGetValue(resourceId, out var asset) || asset == null)
            {
                int extension = resourceId.LastIndexOf('.');
                string path = "SkyKeyYuyan/" + (extension < 0 ? resourceId : resourceId.Substring(0, extension));
                asset = UnityEngine.Resources.Load<TextAsset>(path);
                if (asset == null) throw new FileNotFoundException("Unity 语言资源不存在", resourceId);
                _assets[resourceId] = asset;
            }
            return asset.bytes;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public async Task<byte[]> ReadAsync(string resourceId, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
            {
                _context.Post(() =>
                {
                    if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(); return; }
                    try { completion.TrySetResult(Read(resourceId)); } catch (Exception ex) { completion.TrySetException(ex); }
                });
                return await completion.Task.ConfigureAwait(false);
            }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { _disposed = true; _assets.Clear(); }
    }
}

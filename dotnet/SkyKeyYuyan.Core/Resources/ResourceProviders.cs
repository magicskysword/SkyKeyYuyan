using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 从显式映射的内存资源中读取副本。
    /// </summary>
    public sealed class MemoryResourceProvider : IResourceProvider
    {
        private readonly Dictionary<string, byte[]> _resources;
        /// <summary>
        /// 复制资源映射，防止宿主修改查询中的数据。
        /// </summary>
        public MemoryResourceProvider(IReadOnlyDictionary<string, byte[]>? resources = null)
        { _resources = resources?.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal) ?? new Dictionary<string, byte[]>(); }
        /// <inheritdoc />
        public byte[] Read(string resourceId) => _resources.TryGetValue(resourceId, out var bytes) ? (byte[])bytes.Clone() : throw new FileNotFoundException("语言资源不存在", resourceId);
        /// <inheritdoc />
        public Task<byte[]> ReadAsync(string resourceId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Read(resourceId)); }
    }

    /// <summary>
    /// 从宿主显式映射的文件中读取资源。
    /// </summary>
    public sealed class FileResourceProvider : IResourceProvider
    {
        private readonly IReadOnlyDictionary<string, string> _paths;
        /// <summary>
        /// 创建资源标识到文件路径的映射。
        /// </summary>
        public FileResourceProvider(IReadOnlyDictionary<string, string> paths) { _paths = paths.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal); }
        /// <inheritdoc />
        public byte[] Read(string resourceId) => File.ReadAllBytes(_paths.TryGetValue(resourceId, out var path) ? path : throw new FileNotFoundException("资源标识没有文件映射", resourceId));
        /// <inheritdoc />
        public Task<byte[]> ReadAsync(string resourceId, CancellationToken cancellationToken) => Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return Read(resourceId); }, cancellationToken);
    }
}

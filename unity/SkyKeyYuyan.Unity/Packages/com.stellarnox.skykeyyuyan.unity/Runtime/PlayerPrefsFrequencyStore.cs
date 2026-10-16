using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 在 Unity 主线程以版本化分块记录保存用户词频。
    /// </summary>
    public sealed class PlayerPrefsFrequencyStore : IFrequencyStore
    {
        [Serializable]
        private sealed class Header
        {
            public int formatVersion = 1;
            public long revision;
            public int parts;
        }
        [Serializable]
        private sealed class Word
        {
            public string id;
            public long count;
        }
        [Serializable]
        private sealed class Archive
        {
            public int formatVersion;
            public long revision;
            public string userId, languageId;
            public Word[] words;
        }
        private const int ChunkLength = 16000;
        private readonly ISkyExecutionContext _context;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly string _prefix;

        /// <summary>
        /// 设置主线程上下文及独立的偏好键命名范围。
        /// </summary>
        public PlayerPrefsFrequencyStore(ISkyExecutionContext context, string keyPrefix = "SkyKeyYuyan.Frequency.v1") { _context = context ?? throw new ArgumentNullException(nameof(context)); _prefix = keyPrefix; }
        /// <inheritdoc>
        /// </inheritdoc>
        public FrequencyStoreResult Load(string userId, string languageId)
        {
            try
            {
                EnsureMainThread();
                string prefix = Prefix(userId, languageId);
                if (!PlayerPrefs.HasKey(prefix + ".head")) return Result(userId, languageId, true, "frequency-load");
                var header = JsonUtility.FromJson<Header>(PlayerPrefs.GetString(prefix + ".head"));
                if (header == null || header.formatVersion != 1 || header.parts < 1 || header.parts > 8192 || header.revision < 0) throw new FormatException("词频存档头无效");
                var content = new StringBuilder();
                for (int part = 0; part < header.parts; part++)
                {
                    string key = prefix + "." + header.revision + "." + part;
                    if (!PlayerPrefs.HasKey(key)) throw new FormatException("词频存档分块缺失");
                    content.Append(PlayerPrefs.GetString(key));
                }
                var archive = JsonUtility.FromJson<Archive>(content.ToString());
                if (archive == null || archive.formatVersion != 1 || archive.revision != header.revision || archive.userId != userId || archive.languageId != languageId || archive.words == null) throw new FormatException("词频存档内容与归属不一致");
                var counts = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var word in archive.words)
                {
                    if (word == null || string.IsNullOrWhiteSpace(word.id) || word.count < 0) throw new FormatException("词频词条无效");
                    counts.Add(word.id, word.count);
                }
                return new FrequencyStoreResult(userId, languageId, new ServiceResult(true, "frequency-load"), new FrequencySnapshot(userId, languageId, archive.revision, counts));
            }
            catch (Exception ex) { return Result(userId, languageId, false, "frequency-load", ex.Message); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public FrequencyStoreResult Save(FrequencySnapshot snapshot)
        {
            try
            {
                EnsureMainThread();
                if (snapshot.FormatVersion != 1 || snapshot.Revision < 0 || snapshot.Counts.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)) throw new FormatException("待保存词频版本或计数无效");
                string prefix = Prefix(snapshot.UserId, snapshot.LanguageId);
                Header previous = null;
                if (PlayerPrefs.HasKey(prefix + ".head")) { try { previous = JsonUtility.FromJson<Header>(PlayerPrefs.GetString(prefix + ".head")); } catch { previous = null; } }
                var archive = new Archive { formatVersion = snapshot.FormatVersion, revision = snapshot.Revision, userId = snapshot.UserId, languageId = snapshot.LanguageId, words = snapshot.Counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new Word { id = pair.Key, count = pair.Value }).ToArray() };
                string content = JsonUtility.ToJson(archive);
                if (content.Length > ChunkLength * 8192) throw new FormatException("词频存档超出分块范围");
                int part = 0;
                for (int offset = 0; offset < content.Length;)
                {
                    int end = TextBoundary.Floor(content, Math.Min(content.Length, offset + ChunkLength));
                    PlayerPrefs.SetString(prefix + "." + snapshot.Revision + "." + part++, content.Substring(offset, end - offset));
                    offset = end;
                }
                PlayerPrefs.SetString(prefix + ".head", JsonUtility.ToJson(new Header { revision = snapshot.Revision, parts = part }));
                if (previous != null && previous.parts > 0 && previous.parts <= 8192 && previous.revision != snapshot.Revision)
                    for (int index = 0; index < previous.parts; index++) PlayerPrefs.DeleteKey(prefix + "." + previous.revision + "." + index);
                PlayerPrefs.Save();
                return new FrequencyStoreResult(snapshot.UserId, snapshot.LanguageId, new ServiceResult(true, "frequency-save"), snapshot);
            }
            catch (Exception ex) { return Result(snapshot.UserId, snapshot.LanguageId, false, "frequency-save", ex.Message); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public Task<FrequencyStoreResult> LoadAsync(string userId, string languageId, CancellationToken cancellationToken) => Dispatch(() => Load(userId, languageId), userId, languageId, "frequency-load", cancellationToken);
        /// <inheritdoc>
        /// </inheritdoc>
        public Task<FrequencyStoreResult> SaveAsync(FrequencySnapshot snapshot, CancellationToken cancellationToken) => Dispatch(() => Save(snapshot), snapshot.UserId, snapshot.LanguageId, "frequency-save", cancellationToken);
        private async Task<FrequencyStoreResult> Dispatch(Func<FrequencyStoreResult> action, string user, string language, string stage, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<FrequencyStoreResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => completion.TrySetResult(Result(user, language, false, stage, "存档操作已取消", true))))
            {
                _context.Post(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    try { completion.TrySetResult(action()); } catch (Exception ex) { completion.TrySetResult(Result(user, language, false, stage, ex.Message)); }
                });
                return await completion.Task.ConfigureAwait(false);
            }
        }
        private void EnsureMainThread() { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("PlayerPrefs 同步存档需要 Unity 主线程"); }
        private string Prefix(string userId, string languageId) => _prefix + "." + Encode(userId) + "." + Encode(languageId);
        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static FrequencyStoreResult Result(string user, string language, bool succeeded, string stage, string reason = null, bool cancelled = false) => new FrequencyStoreResult(user, language, new ServiceResult(succeeded, stage, reason, cancelled));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 支持草稿及独立宿主的内存文本目标，以 UTF-16 单元计算长度。
    /// </summary>
    public sealed class MemoryTextTarget : ITextTarget, ITextOperationCache
    {
        private TextSnapshot _snapshot;
        private readonly Dictionary<OperationId, Dictionary<int, TextEditResult>> _edits = new Dictionary<OperationId, Dictionary<int, TextEditResult>>();
        private readonly Dictionary<OperationId, TextNavigationResult> _navigation = new Dictionary<OperationId, TextNavigationResult>();
        private bool _valid = true;

        /// <summary>
        /// 创建文本目标，零长度上限表示不限长度。
        /// </summary>
        public MemoryTextTarget(string text = "", int anchor = -1, int caret = -1, int maxLength = 0, bool singleLine = false, Func<string, bool>? validator = null, object? identity = null)
        {
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
            _snapshot = new TextSnapshot(text, anchor < 0 ? text.Length : anchor, caret < 0 ? text.Length : caret, 0);
            MaxLength = maxLength; SingleLine = singleLine; Validator = validator; Identity = identity ?? this;
        }

        /// <inheritdoc />
        public object Identity { get; }
        /// <summary>
        /// 正文长度上限，零表示不限长度。
        /// </summary>
        public int MaxLength { get; set; }
        /// <summary>
        /// 是否拒绝包含换行的编辑。
        /// </summary>
        public bool SingleLine { get; set; }
        /// <summary>
        /// 对最终正文执行的内容验证。
        /// </summary>
        public Func<string, bool>? Validator { get; set; }
        /// <inheritdoc />
        public event Action<TextTargetChange>? Changed;
        /// <inheritdoc />
        public TextSnapshot ReadSnapshot() => _snapshot;

        /// <inheritdoc />
        public TextEditResult ApplyEdit(TextEditRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_edits.TryGetValue(request.Operation, out var parts) && parts.TryGetValue(request.Part, out var known)) return known;
            if (!_valid) return new TextEditResult(TextEditStatus.InvalidTarget, _snapshot);
            if (request.ExpectedVersion != _snapshot.Version) return new TextEditResult(TextEditStatus.VersionConflict, _snapshot);
            if (request.Start < 0 || request.Length < 0 || request.Start > _snapshot.Text.Length || request.Length > _snapshot.Text.Length - request.Start)
                return new TextEditResult(TextEditStatus.Rejected, _snapshot, reason: "替换范围超出正文");

            int start = TextBoundary.Floor(_snapshot.Text, request.Start);
            int end = request.Length == 0 ? start : TextBoundary.Ceiling(_snapshot.Text, request.Start + request.Length);
            string accepted = request.Text;
            if (SingleLine && (accepted.IndexOf('\n') >= 0 || accepted.IndexOf('\r') >= 0)) return new TextEditResult(TextEditStatus.Rejected, _snapshot, reason: "单行目标不能接受换行");
            if (MaxLength > 0) accepted = TextBoundary.Prefix(accepted, Math.Max(0, MaxLength - (_snapshot.Text.Length - (end - start))));
            string result = _snapshot.Text.Substring(0, start) + accepted + _snapshot.Text.Substring(end);
            if (Validator != null && !Validator(result)) return new TextEditResult(TextEditStatus.Rejected, _snapshot, reason: "正文验证未通过");

            ReplaceSnapshot(result, start + accepted.Length, start + accepted.Length);
            var editResult = new TextEditResult(accepted.Length < request.Text.Length ? TextEditStatus.Truncated : TextEditStatus.Accepted, _snapshot, accepted, start, end - start);
            if (parts == null) _edits.Add(request.Operation, parts = new Dictionary<int, TextEditResult>());
            parts.Add(request.Part, editResult);
            return editResult;
        }

        /// <inheritdoc />
        public TextNavigationResult Navigate(TextNavigationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_navigation.TryGetValue(request.Operation, out var known)) return known;
            if (!_valid) return new TextNavigationResult(TextEditStatus.InvalidTarget, _snapshot);
            if (request.ExpectedVersion != _snapshot.Version) return new TextNavigationResult(TextEditStatus.VersionConflict, _snapshot);
            int caret = _snapshot.Caret;
            int anchor = _snapshot.Anchor;
            string text = _snapshot.Text;
            switch (request.Direction)
            {
                case TextNavigation.Left: caret = !request.Extend && _snapshot.SelectionLength > 0 ? _snapshot.SelectionStart : TextBoundary.Previous(text, caret); break;
                case TextNavigation.Right: caret = !request.Extend && _snapshot.SelectionLength > 0 ? _snapshot.SelectionStart + _snapshot.SelectionLength : TextBoundary.Next(text, caret); break;
                case TextNavigation.Home: caret = LineStart(text, caret); break;
                case TextNavigation.End: caret = LineEnd(text, caret); break;
                case TextNavigation.DocumentStart: caret = 0; break;
                case TextNavigation.DocumentEnd: caret = text.Length; break;
                case TextNavigation.SelectAll: anchor = 0; caret = text.Length; break;
                case TextNavigation.Up: if (!SingleLine) caret = Vertical(text, caret, -1); break;
                case TextNavigation.Down: if (!SingleLine) caret = Vertical(text, caret, 1); break;
            }
            if (!request.Extend && request.Direction != TextNavigation.SelectAll) anchor = caret;
            ReplaceSnapshot(text, anchor, caret);
            var result = new TextNavigationResult(TextEditStatus.Accepted, _snapshot);
            _navigation.Add(request.Operation, result);
            return result;
        }

        /// <inheritdoc />
        public void ReleaseSession(Guid sessionId)
        {
            foreach (var operation in _edits.Keys.Where(key => key.SessionId == sessionId).ToArray()) _edits.Remove(operation);
            foreach (var operation in _navigation.Keys.Where(key => key.SessionId == sessionId).ToArray()) _navigation.Remove(operation);
        }

        /// <summary>
        /// 应用宿主正文修改，并通知活动会话。
        /// </summary>
        public void SetExternalText(string text, int anchor = -1, int caret = -1)
        {
            if (!_valid) throw new ObjectDisposedException(nameof(MemoryTextTarget));
            var previous = _snapshot;
            ReplaceSnapshot(text, anchor < 0 ? text.Length : anchor, caret < 0 ? text.Length : caret);
            if (!ReferenceEquals(previous, _snapshot)) Changed?.Invoke(new TextTargetChange(_snapshot));
        }

        /// <summary>
        /// 使目标失效，并通知活动会话。
        /// </summary>
        public void Invalidate()
        {
            if (!_valid) return;
            _valid = false;
            Changed?.Invoke(new TextTargetChange(_snapshot, true));
        }

        private void ReplaceSnapshot(string text, int anchor, int caret)
        {
            anchor = TextBoundary.Floor(text, anchor); caret = TextBoundary.Floor(text, caret);
            if (_snapshot.Text == text && _snapshot.Anchor == anchor && _snapshot.Caret == caret) return;
            _snapshot = new TextSnapshot(text, anchor, caret, _snapshot.Version + 1);
        }

        private static int LineStart(string text, int caret) => caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        private static int LineEnd(string text, int caret) { int end = text.IndexOf('\n', caret); return end < 0 ? text.Length : end; }
        private static int Vertical(string text, int caret, int direction)
        {
            int start = LineStart(text, caret), column = caret - start;
            if (direction < 0)
            {
                if (start == 0) return caret;
                int previousStart = LineStart(text, start - 1);
                return TextBoundary.Floor(text, Math.Min(previousStart + column, start - 1));
            }
            int end = LineEnd(text, caret);
            if (end == text.Length) return caret;
            return TextBoundary.Floor(text, Math.Min(end + 1 + column, LineEnd(text, end + 1)));
        }
    }
}

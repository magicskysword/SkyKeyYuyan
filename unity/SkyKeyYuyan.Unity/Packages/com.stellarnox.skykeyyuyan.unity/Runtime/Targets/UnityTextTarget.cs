using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 可轮询宿主状态并协调原生键盘的 Unity 文本目标。
    /// </summary>
    public interface IUnityTextTarget : ITextTarget, ITextOperationCache, IDisposable
    {
        /// <summary>
        /// 宿主控件是否仍有效。
        /// </summary>
        bool IsAlive { get; }
        /// <summary>
        /// 宿主控件是否进入编辑态。
        /// </summary>
        bool IsEditing { get; }
        /// <summary>
        /// 同步宿主正文和选区，报告外部修改及失效。
        /// </summary>
        void Poll();
        /// <summary>
        /// 设置 SKY 是否接管该目标的键盘输入。
        /// </summary>
        void SetKeyboardOwned(bool owned);
        /// <summary>
        /// 结束宿主控件的原生编辑态。
        /// </summary>
        void EndEditing();
    }
    /// <summary>
    /// 以确定的编辑计划、原生验证和回执缓存协调 Unity 控件。
    /// </summary>
    public abstract class UnityTextTarget : IUnityTextTarget
    {
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<OperationId, Dictionary<int, TextEditResult>> _edits = new Dictionary<OperationId, Dictionary<int, TextEditResult>>();
        private readonly Dictionary<OperationId, TextNavigationResult> _navigation = new Dictionary<OperationId, TextNavigationResult>();
        private MemoryTextTarget _mirror;
        private bool _disposed, _invalidated, _writing;
        /// <summary>
        /// 使用宿主控件引用作为跨适配器的目标身份。
        /// </summary>
        protected UnityTextTarget(object identity) { Identity = identity ?? throw new ArgumentNullException(nameof(identity)); }
        /// <inheritdoc>
        /// </inheritdoc>
        public object Identity { get; }
        /// <inheritdoc>
        /// </inheritdoc>
        public bool IsAlive => !_disposed && NativeAlive;
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract bool IsEditing { get; }
        /// <summary>
        /// 原生对象的有效性。
        /// </summary>
        protected abstract bool NativeAlive { get; }
        /// <summary>
        /// 原生正文长度上限。
        /// </summary>
        protected abstract int CharacterLimit { get; }
        /// <summary>
        /// 原生控件是否为单行。
        /// </summary>
        protected abstract bool SingleLine { get; }
        /// <summary>
        /// 连续垂直导航的期望横坐标。
        /// </summary>
        protected float? PreferredX { get; set; }
        /// <summary>
        /// 读取宿主正文。
        /// </summary>
        protected abstract string ReadText();
        /// <summary>
        /// 读取原生选区并转换为 UTF-16 偏移。
        /// </summary>
        protected abstract void ReadSelection(out int anchor, out int caret);
        /// <summary>
        /// 写入已经验证的完整正文和选区，不再重复验证。
        /// </summary>
        protected abstract void WriteText(string text, int anchor, int caret);
        /// <summary>
        /// 写入原生选区。
        /// </summary>
        protected abstract void WriteSelection(int anchor, int caret);
        /// <summary>
        /// 通知宿主已完成的正文变化。
        /// </summary>
        protected abstract void NotifyTextChanged(string text);
        /// <summary>
        /// 验证可容纳的插入文本，返回实际文本或 null 表示拒绝。
        /// </summary>
        protected virtual string ValidateInsertion(TextSnapshot snapshot, int start, int length, string text) => text;
        /// <summary>
        /// 按实际排版计算相邻排的光标位置。
        /// </summary>
        protected virtual int VerticalPosition(TextSnapshot snapshot, int direction)
        {
            var target = new MemoryTextTarget(snapshot.Text, snapshot.Anchor, snapshot.Caret);
            return target.Navigate(new TextNavigationRequest(default, 0, direction < 0 ? TextNavigation.Up : TextNavigation.Down)).Snapshot.Caret;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public event Action<TextTargetChange> Changed;
        /// <summary>
        /// 控件验证或通知阶段的独立诊断。
        /// </summary>
        public event Action<SkyDiagnostic> Diagnostic;

        /// <inheritdoc>
        /// </inheritdoc>
        public TextSnapshot ReadSnapshot() { Poll(); if (!IsAlive) throw new ObjectDisposedException(GetType().Name); return _mirror.ReadSnapshot(); }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Poll()
        {
            EnsureMainThread();
            if (!IsAlive) { Invalidate(); return; }
            Synchronize(!_writing);
        }
        private TextSnapshot Synchronize(bool notifyExternal)
        {
            string text = ReadText() ?? ""; ReadSelection(out int anchor, out int caret);
            if (_mirror == null) { _mirror = new MemoryTextTarget(text, anchor, caret); return _mirror.ReadSnapshot(); }
            var before = _mirror.ReadSnapshot();
            _mirror.SetExternalText(text, anchor, caret);
            var after = _mirror.ReadSnapshot();
            if (notifyExternal && before.Text != text) { PreferredX = null; RaiseChange(new TextTargetChange(after)); }
            return after;
        }
        /// <summary>
        /// 从原生变化事件中同步外部修改。
        /// </summary>
        protected void NativeChanged() { if (!_writing && IsAlive) Poll(); }
        /// <inheritdoc>
        /// </inheritdoc>
        public TextEditResult ApplyEdit(TextEditRequest request)
        {
            EnsureMainThread();
            if (_edits.TryGetValue(request.Operation, out var parts) && parts.TryGetValue(request.Part, out var known)) return known;
            if (!IsAlive) { Invalidate(); return new TextEditResult(TextEditStatus.InvalidTarget, _mirror?.ReadSnapshot() ?? new TextSnapshot("", 0, 0, 0)); }
            var before = ReadSnapshot();
            if (!IsAlive) return new TextEditResult(TextEditStatus.InvalidTarget, before);
            if (before.Version != request.ExpectedVersion) return new TextEditResult(TextEditStatus.VersionConflict, before);
            var plan = Plan(before, request, request.Text);
            if (!plan.Applied) return new TextEditResult(plan.Status, before, reason: plan.Reason);
            string validated;
            try { validated = ValidateInsertion(before, plan.Start, plan.Length, plan.AcceptedText); }
            catch (Exception ex) { Report("validation", ex.Message, request.Operation); return new TextEditResult(TextEditStatus.Rejected, before, reason: ex.Message); }
            if (validated == null) return new TextEditResult(TextEditStatus.Rejected, before, reason: "控件验证未接受插入文本");
            var current = ReadSnapshot();
            if (current.Version != before.Version) return new TextEditResult(TextEditStatus.VersionConflict, current);
            var accepted = Plan(before, request, validated);
            if (!accepted.Applied) return new TextEditResult(accepted.Status, before, reason: accepted.Reason);
            var status = plan.Status == TextEditStatus.Truncated || accepted.Status == TextEditStatus.Truncated ? TextEditStatus.Truncated : TextEditStatus.Accepted;
            PreferredX = null;
            try
            {
                _writing = true;
                WriteText(accepted.Snapshot.Text, accepted.Snapshot.Anchor, accepted.Snapshot.Caret);
                if (before.Text != accepted.Snapshot.Text)
                    try { NotifyTextChanged(accepted.Snapshot.Text); } catch (Exception ex) { Report("notification", ex.Message, request.Operation); }
                if (!IsAlive) { Invalidate(); return new TextEditResult(TextEditStatus.InvalidTarget, before); }
                var after = Synchronize(false);
                if (after.Text != accepted.Snapshot.Text)
                {
                    RaiseChange(new TextTargetChange(after));
                    status = TextEditStatus.VersionConflict;
                }
                var result = new TextEditResult(status, after, accepted.AcceptedText, accepted.Start, accepted.Length);
                if (parts == null) _edits.Add(request.Operation, parts = new Dictionary<int, TextEditResult>());
                parts[request.Part] = result;
                return result;
            }
            catch (Exception ex)
            {
                Report("text", ex.Message, request.Operation);
                TextSnapshot after = before;
                if (IsAlive) { try { after = Synchronize(false); } catch { } }
                return new TextEditResult(IsAlive ? TextEditStatus.Unknown : TextEditStatus.InvalidTarget, after, reason: ex.Message);
            }
            finally { _writing = false; }
        }
        private TextEditResult Plan(TextSnapshot before, TextEditRequest request, string input)
        {
            var preview = new MemoryTextTarget(before.Text, before.Anchor, before.Caret, Math.Max(0, CharacterLimit), SingleLine);
            return preview.ApplyEdit(new TextEditRequest(request.Operation, 0, request.Start, request.Length, input, request.Part));
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public TextNavigationResult Navigate(TextNavigationRequest request)
        {
            EnsureMainThread();
            if (_navigation.TryGetValue(request.Operation, out var known)) return known;
            if (!IsAlive) { Invalidate(); return new TextNavigationResult(TextEditStatus.InvalidTarget, _mirror?.ReadSnapshot() ?? new TextSnapshot("", 0, 0, 0)); }
            var before = ReadSnapshot();
            if (!IsAlive) return new TextNavigationResult(TextEditStatus.InvalidTarget, before);
            if (before.Version != request.ExpectedVersion) return new TextNavigationResult(TextEditStatus.VersionConflict, before);
            var preview = new MemoryTextTarget(before.Text, before.Anchor, before.Caret, singleLine: SingleLine);
            var expected = preview.Navigate(new TextNavigationRequest(request.Operation, 0, request.Direction, request.Extend)).Snapshot;
            int anchor = expected.Anchor, caret = expected.Caret;
            if (!SingleLine && (request.Direction == TextNavigation.Up || request.Direction == TextNavigation.Down))
            {
                caret = TextBoundary.Floor(before.Text, VerticalPosition(before, request.Direction == TextNavigation.Up ? -1 : 1));
                anchor = request.Extend ? before.Anchor : caret;
            }
            else PreferredX = null;
            try
            {
                _writing = true; WriteSelection(anchor, caret);
                var result = new TextNavigationResult(TextEditStatus.Accepted, Synchronize(false));
                _navigation.Add(request.Operation, result); return result;
            }
            catch (Exception ex) { Report("navigation", ex.Message, request.Operation); return new TextNavigationResult(TextEditStatus.Unknown, ReadSnapshot()); }
            finally { _writing = false; }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void ReleaseSession(Guid sessionId)
        {
            foreach (var key in _edits.Keys.Where(key => key.SessionId == sessionId).ToArray()) _edits.Remove(key);
            foreach (var key in _navigation.Keys.Where(key => key.SessionId == sessionId).ToArray()) _navigation.Remove(key);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract void SetKeyboardOwned(bool owned);
        /// <inheritdoc>
        /// </inheritdoc>
        public abstract void EndEditing();
        /// <summary>
        /// 解除原生控件事件订阅并释放适配器临时对象。
        /// </summary>
        protected virtual void DisposeNative() { }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            EnsureMainThread(); SetKeyboardOwned(false); DisposeNative(); _disposed = true; Invalidate(); _edits.Clear(); _navigation.Clear();
        }
        private void Invalidate()
        {
            if (_invalidated) return;
            _invalidated = true; RaiseChange(new TextTargetChange(_mirror?.ReadSnapshot() ?? new TextSnapshot("", 0, 0, 0), true));
        }
        private void EnsureMainThread() { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Unity 文本目标需要在创建线程操作"); }
        private void RaiseChange(TextTargetChange change)
        {
            if (Changed == null) return;
            foreach (Action<TextTargetChange> handler in Changed.GetInvocationList()) try { handler(change); } catch (Exception ex) { Report("target-notification", ex.Message); }
        }
        private void Report(string stage, string reason, OperationId? operation = null)
        {
            var diagnostic = new SkyDiagnostic(stage, reason, operation: operation);
            if (Diagnostic == null) { Debug.LogWarning("SKY " + stage + ": " + reason); return; }
            foreach (Action<SkyDiagnostic> handler in Diagnostic.GetInvocationList()) try { handler(diagnostic); } catch { }
        }
    }
}

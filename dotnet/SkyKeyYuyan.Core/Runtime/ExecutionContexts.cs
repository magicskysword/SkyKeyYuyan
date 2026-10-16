using System;
using System.Collections.Generic;
using System.Threading;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 在宿主选定的执行上下文中串行执行会话工作。
    /// </summary>
    public interface ISkyExecutionContext
    {
        /// <summary>
        /// 将工作排入执行队列，事件回调中的新工作不重入当前工作。
        /// </summary>
        void Post(Action action);
    }

    /// <summary>
    /// 由首个投递线程排空队列的串行执行上下文，适合独立托管宿主。
    /// </summary>
    public sealed class SerialExecutionContext : ISkyExecutionContext
    {
        private readonly object _gate = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private bool _draining;
        /// <inheritdoc>
        /// </inheritdoc>
        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_gate) { _queue.Enqueue(action); if (_draining) return; _draining = true; }
            try
            {
                while (true)
                {
                    Action next;
                    lock (_gate) { if (_queue.Count == 0) { _draining = false; return; } next = _queue.Dequeue(); }
                    next();
                }
            }
            catch { lock (_gate) _draining = false; throw; }
        }
    }

    /// <summary>
    /// 由宿主线程显式排空队列的执行上下文，适合游戏主循环。
    /// </summary>
    public sealed class PumpExecutionContext : ISkyExecutionContext
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly object _gate = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private bool _draining;
        /// <inheritdoc>
        /// </inheritdoc>
        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_gate) _queue.Enqueue(action);
            if (Thread.CurrentThread.ManagedThreadId == _threadId) Drain();
        }
        /// <summary>
        /// 在创建线程执行所有已投递工作。
        /// </summary>
        public void Drain()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new InvalidOperationException("执行队列只能在宿主线程排空");
            if (_draining) return;
            _draining = true;
            try
            {
                while (true)
                {
                    Action next;
                    lock (_gate) { if (_queue.Count == 0) return; next = _queue.Dequeue(); }
                    next();
                }
            }
            finally { _draining = false; }
        }
    }
}

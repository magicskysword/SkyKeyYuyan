using System;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 由宿主主循环推进的可选输入后端。
    /// </summary>
    public interface ISkyInputDriver : IDisposable
    {
        /// <summary>
        /// 使用不受游戏时间缩放影响的时间推进一次设备采样。
        /// </summary>
        void Poll(double time);
    }
    /// <summary>
    /// 对方向和编辑按键提供一致的按住重复规则。
    /// </summary>
    public sealed class SkyInputRepeat
    {
        private bool _held;
        private double _next;
        /// <summary>
        /// 初次按住后的重复延迟，单位秒。
        /// </summary>
        public double Delay { get; set; } = 0.4;
        /// <summary>
        /// 后续重复间隔，单位秒。
        /// </summary>
        public double Interval { get; set; } = 0.075;
        /// <summary>
        /// 当前采样是否应产生一次动作。
        /// </summary>
        public bool Pulse(bool held, double time, bool repeat = true)
        {
            if (!held) { _held = false; return false; }
            if (!_held) { _held = true; _next = time + Math.Max(0, Delay); return true; }
            if (!repeat || time < _next) return false;
            _next = time + Math.Max(0.001, Interval); return true;
        }
        /// <summary>
        /// 释放已采样的按住状态。
        /// </summary>
        public void Reset() { _held = false; }
    }
}

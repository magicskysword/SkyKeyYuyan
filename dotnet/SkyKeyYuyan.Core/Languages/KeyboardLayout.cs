using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 平台无关的虚拟按键定义。
    /// </summary>
    public sealed class KeyboardKey
    {
        /// <summary>
        /// 创建具有固定位置、文本输出和功能动作的按键。
        /// </summary>
        public KeyboardKey(string id, string label, string output, string alternate, int row, int column, string action = "input")
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(action) || row < 0 || column < 0) throw new ArgumentException("按键身份、动作和位置无效");
            Id = id; Label = label ?? throw new ArgumentNullException(nameof(label)); Output = output ?? throw new ArgumentNullException(nameof(output));
            Alternate = alternate ?? throw new ArgumentNullException(nameof(alternate)); Row = row; Column = column; Action = action;
        }
        /// <summary>
        /// 布局内稳定的按键身份。
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// 显示标签。
        /// </summary>
        public string Label { get; }
        /// <summary>
        /// 普通状态的输出。
        /// </summary>
        public string Output { get; }
        /// <summary>
        /// Shift 状态的输出。
        /// </summary>
        public string Alternate { get; }
        /// <summary>
        /// 逻辑排号。
        /// </summary>
        public int Row { get; }
        /// <summary>
        /// 逻辑列号。
        /// </summary>
        public int Column { get; }
        /// <summary>
        /// 文本输入或功能动作。
        /// </summary>
        public string Action { get; }
    }
    /// <summary>
    /// 可共享的不可变键盘布局及方向导航。
    /// </summary>
    public sealed class KeyboardLayout
    {
        private readonly Dictionary<string, KeyboardKey> _byId;
        /// <summary>
        /// 复制布局数据，并检查身份和位置唯一性。
        /// </summary>
        public KeyboardLayout(string languageId, string dataVersion, IEnumerable<KeyboardKey> keys)
        {
            if (string.IsNullOrWhiteSpace(languageId) || string.IsNullOrWhiteSpace(dataVersion)) throw new ArgumentException("布局语言和数据版本不能为空");
            var entries = keys?.OrderBy(key => key.Row).ThenBy(key => key.Column).ToArray() ?? throw new ArgumentNullException(nameof(keys));
            if (entries.Length == 0 || entries.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length
                || entries.Select(key => Tuple.Create(key.Row, key.Column)).Distinct().Count() != entries.Length) throw new ArgumentException("布局不能为空且按键身份和位置必须唯一");
            LanguageId = languageId; DataVersion = dataVersion; Keys = Array.AsReadOnly(entries);
            _byId = entries.ToDictionary(key => key.Id, StringComparer.Ordinal);
        }
        /// <summary>
        /// 对应语言。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 数据版本。
        /// </summary>
        public string DataVersion { get; }
        /// <summary>
        /// 按排和列排序的按键。
        /// </summary>
        public IReadOnlyList<KeyboardKey> Keys { get; }
        /// <summary>
        /// 取得一个按键。
        /// </summary>
        public KeyboardKey this[string id] => _byId[id];
        /// <summary>
        /// 同排横向循环，相邻排选择距离当前列最近的按键。
        /// </summary>
        public string Navigate(string currentId, TextNavigation direction)
        {
            if (!_byId.TryGetValue(currentId, out var current)) return Keys[0].Id;
            if (direction == TextNavigation.Left || direction == TextNavigation.Right)
            {
                var row = Keys.Where(key => key.Row == current.Row).ToArray(); int position = Array.IndexOf(row, current);
                return row[(position + (direction == TextNavigation.Left ? row.Length - 1 : 1)) % row.Length].Id;
            }
            if (direction != TextNavigation.Up && direction != TextNavigation.Down) throw new ArgumentOutOfRangeException(nameof(direction));
            var rows = Keys.Select(key => key.Row).Distinct().ToArray(); int index = Array.IndexOf(rows, current.Row);
            int next = rows[(index + (direction == TextNavigation.Up ? rows.Length - 1 : 1)) % rows.Length];
            return Keys.Where(key => key.Row == next).OrderBy(key => Math.Abs((long)key.Column - current.Column)).ThenBy(key => key.Column).First().Id;
        }
        /// <summary>
        /// 读取版本化 UTF-8 布局资源并核对语言身份。
        /// </summary>
        public static KeyboardLayout Parse(byte[] bytes, string expectedLanguage)
        {
            using (var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
            {
                string[]? header = reader.ReadLine()?.TrimStart('\ufeff').Split('\t');
                if (header == null || header.Length != 4 || header[0] != "SKYKEYS" || header[1] != "1" || header[2] != expectedLanguage)
                    throw new InvalidDataException("键盘布局身份或格式版本不兼容");
                var keys = new List<KeyboardKey>(); string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    var fields = line.Split('\t');
                    if (fields.Length != 7 || !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int row)
                        || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int column)) throw new InvalidDataException("键盘按键结构无效");
                    keys.Add(new KeyboardKey(fields[0], fields[1], fields[2], fields[3], row, column, fields[6]));
                }
                return new KeyboardLayout(header[2], header[3], keys);
            }
        }
        /// <summary>
        /// 无外部资源依赖的基础英语布局。
        /// </summary>
        public static KeyboardLayout BasicEnglish { get; } = CreateBasicEnglish();
        private static KeyboardLayout CreateBasicEnglish()
        {
            var keys = new List<KeyboardKey>();
            var rows = new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm", ",.?!:;()-'" };
            for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                {
                    string output = rows[row][column].ToString();
                    keys.Add(new KeyboardKey(row == 4 ? "punctuation-" + column : "key-" + row + "-" + column, output, output, output.ToUpperInvariant(), row, column));
                }
            var actions = new[] { "space", "shift", "backspace", "newline", "language", "finish", "cancel", "left", "right", "up", "down", "delete", "selectall", "clear", "completion", "retry" };
            var labels = new[] { "空格", "Shift", "退格", "换行", "语言", "完成", "取消", "左移", "右移", "上移", "下移", "删除", "全选", "清空", "补全", "重试转换" };
            for (int index = 0; index < actions.Length; index++)
                keys.Add(new KeyboardKey(actions[index], labels[index], index == 0 ? " " : "", index == 0 ? " " : "", index < 7 ? 5 : 6, index < 7 ? index : index - 7, index == 0 ? "input" : actions[index]));
            return new KeyboardLayout("en", "builtin.1", keys);
        }
    }
    /// <summary>
    /// 提供语言默认布局的可选模块能力。
    /// </summary>
    public interface IKeyboardLayoutSource
    {
        /// <summary>
        /// 从宿主 Provider 准备可共享的布局。
        /// </summary>
        KeyboardLayout LoadKeyboard(IResourceProvider resources);
    }
    /// <summary>
    /// 会话固定的语言菜单条目。
    /// </summary>
    public sealed class LanguageInfo
    {
        internal LanguageInfo(ILanguageModule module) { LanguageId = module.LanguageId; EngineId = module.EngineId; DisplayName = module.DisplayName; Capabilities = module.Capabilities; }
        /// <summary>
        /// 语言标识。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 实现标识。
        /// </summary>
        public string EngineId { get; }
        /// <summary>
        /// 菜单名称。
        /// </summary>
        public string DisplayName { get; }
        /// <summary>
        /// 候选和设置能力。
        /// </summary>
        public LanguageCapabilities Capabilities { get; }
    }
}

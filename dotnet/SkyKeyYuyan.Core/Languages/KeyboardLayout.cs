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
            : this(id, label, output, alternate, row, column, action, 1) { }
        /// <summary>
        /// 创建可占据多个逻辑列的按键。
        /// </summary>
        public KeyboardKey(string id, string label, string output, string alternate, int row, int column, string action, int columnSpan)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(action) || row < 0 || column < 0 || columnSpan < 1) throw new ArgumentException("按键身份、动作和位置无效");
            Id = id; Label = label ?? throw new ArgumentNullException(nameof(label)); Output = output ?? throw new ArgumentNullException(nameof(output));
            Alternate = alternate ?? throw new ArgumentNullException(nameof(alternate)); Row = row; Column = column; Action = action; ColumnSpan = columnSpan;
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
        /// 占据的逻辑列数。
        /// </summary>
        public int ColumnSpan { get; }
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
        private readonly Dictionary<string, string[]> _neighbors;
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
            ColumnCount = entries.Max(key => (long)key.Column + key.ColumnSpan);
            RowCount = (long)entries[entries.Length - 1].Row + 1;
            _neighbors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var rows = entries.GroupBy(key => key.Row).Select(group => group.ToArray()).ToArray();
            for (int row = 0; row < rows.Length; row++)
            {
                var keysInRow = rows[row];
                for (int column = 0; column < keysInRow.Length; column++)
                {
                    var key = keysInRow[column];
                    if (column > 0 && (long)keysInRow[column - 1].Column + keysInRow[column - 1].ColumnSpan > key.Column) throw new ArgumentException("同排按键不能重叠");
                    _neighbors.Add(key.Id, new[]
                    {
                        keysInRow[(column + keysInRow.Length - 1) % keysInRow.Length].Id,
                        keysInRow[(column + 1) % keysInRow.Length].Id,
                        Nearest(rows[(row + rows.Length - 1) % rows.Length], key),
                        Nearest(rows[(row + 1) % rows.Length], key)
                    });
                }
            }
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
        /// 布局的逻辑总宽度。
        /// </summary>
        public long ColumnCount { get; }
        /// <summary>
        /// 包含空排在内的逻辑排数。
        /// </summary>
        public long RowCount { get; }
        /// <summary>
        /// 取得一个按键。
        /// </summary>
        public KeyboardKey this[string id] => _byId[id];
        /// <summary>
        /// 同排横向循环，相邻排选择距离当前键帽中心最近的按键。
        /// </summary>
        public string Navigate(string currentId, TextNavigation direction)
        {
            if (!_neighbors.TryGetValue(currentId, out var neighbors)) return Keys[0].Id;
            switch (direction)
            {
                case TextNavigation.Left: return neighbors[0];
                case TextNavigation.Right: return neighbors[1];
                case TextNavigation.Up: return neighbors[2];
                case TextNavigation.Down: return neighbors[3];
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
        private static string Nearest(KeyboardKey[] row, KeyboardKey current)
        {
            long center = (long)current.Column * 2 + current.ColumnSpan, distance = long.MaxValue;
            string best = row[0].Id;
            foreach (var key in row)
            {
                long candidate = Math.Abs((long)key.Column * 2 + key.ColumnSpan - center);
                if (candidate < distance) { best = key.Id; distance = candidate; }
            }
            return best;
        }
        /// <summary>
        /// 读取版本化 UTF-8 布局资源并核对语言身份。
        /// </summary>
        public static KeyboardLayout Parse(byte[] bytes, string expectedLanguage)
        {
            using (var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
            {
                string[]? header = reader.ReadLine()?.TrimStart('\ufeff').Split('\t');
                if (header == null || header.Length != 4 || header[0] != "SKYKEYS" || header[1] != "1" && header[1] != "2" || header[2] != expectedLanguage)
                    throw new InvalidDataException("键盘布局身份或格式版本不兼容");
                var keys = new List<KeyboardKey>(); string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    var fields = line.Split('\t');
                    bool spans = header[1] == "2";
                    if (fields.Length != (spans ? 8 : 7) || !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int row)
                        || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int column)) throw new InvalidDataException("键盘按键结构无效");
                    int span = 1;
                    if (spans && !int.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out span)) throw new InvalidDataException("键盘按键宽度无效");
                    keys.Add(new KeyboardKey(fields[0], fields[1], fields[2], fields[3], row, column, fields[6], span));
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
            var rows = new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
            for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                {
                    string output = rows[row][column].ToString();
                    string alternate = row == 0 ? "!@#$%^&*()"[column].ToString() : output.ToUpperInvariant();
                    keys.Add(new KeyboardKey("key-" + row + "-" + column, output.ToUpperInvariant(), output, alternate, row, column * 2 + (row == 2 ? 1 : row == 3 ? 4 : 0), "input", 2));
                }
            void Input(string id, string output, string alternate, int row, int column) => keys.Add(new KeyboardKey(id, output, output, alternate, row, column, "input", 2));
            void Action(string id, string label, int row, int column, int span) => keys.Add(new KeyboardKey(id, label, id == "space" ? " " : "", id == "space" ? " " : "", row, column, id == "space" ? "input" : id, span));
            Input("minus", "-", "_", 0, 20); Input("equals", "=", "+", 0, 22); Action("backspace", "退格", 0, 24, 4);
            Input("open-bracket", "[", "{", 1, 20); Input("close-bracket", "]", "}", 1, 22); Action("delete", "删除", 1, 24, 4);
            Input("semicolon", ";", ":", 2, 19); Input("quote", "'", "\"", 2, 21); Action("newline", "换行", 2, 23, 5);
            Action("shift", "Shift", 3, 0, 4); Input("punctuation-0", ",", "<", 3, 18); Input("punctuation-1", ".", ">", 3, 20); Input("punctuation-2", "/", "?", 3, 22); Action("clear", "清空", 3, 24, 4);
            Action("language", "语言", 4, 0, 4); Action("completion", "补全", 4, 4, 4); Action("space", "空格", 4, 8, 12);
            Action("left", "←", 4, 20, 2); Action("up", "↑", 4, 22, 2); Action("down", "↓", 4, 24, 2); Action("right", "→", 4, 26, 2);
            Action("selectall", "全选", 5, 0, 5); Action("retry", "重试转换", 5, 5, 6); Action("cancel", "取消", 5, 11, 8); Action("finish", "完成", 5, 19, 9);
            return new KeyboardLayout("en", "builtin.2", keys);
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

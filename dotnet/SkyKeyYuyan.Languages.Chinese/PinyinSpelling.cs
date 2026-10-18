using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SkyKeyYuyan.Languages.Chinese
{
    /// <summary>
    /// 规范化拼音声调、音节分隔符和 ü 的键盘表示。
    /// </summary>
    public static class PinyinSpelling
    {
        /// <summary>
        /// 将读音规范化为以空格分隔的无声调小写音节，ü 使用 v。
        /// </summary>
        public static string Normalize(string spelling)
        {
            bool canonical = spelling.Length > 0 && spelling[0] != ' ' && spelling[spelling.Length - 1] != ' ';
            for (int index = 0; canonical && index < spelling.Length; index++)
            {
                char value = spelling[index];
                canonical = value >= 'a' && value <= 'z' || value == ' ' && index > 0 && spelling[index - 1] != ' ';
            }
            if (canonical) return spelling;
            string decomposed = spelling.ToLowerInvariant().Normalize(NormalizationForm.FormD).Replace("u\u0308", "v").Replace("u:", "v");
            var result = new StringBuilder(decomposed.Length);
            foreach (char value in decomposed)
            {
                if (char.GetUnicodeCategory(value) == UnicodeCategory.NonSpacingMark || value >= '1' && value <= '5') continue;
                if (char.IsWhiteSpace(value) || value == '\'' || value == '’') result.Append(' ');
                else if (value >= 'a' && value <= 'z') result.Append(value);
                else throw new ArgumentException("拼音读音包含无效字符", nameof(spelling));
            }
            string normalized = string.Join(" ", result.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0) throw new ArgumentException("拼音读音不能为空", nameof(spelling));
            return normalized;
        }
        internal static string Flatten(string spelling) => spelling.Replace(" ", "");
        internal static bool IsAbbreviation(string spelling, int start, int length) => length == 1
            || length == 2 && spelling[start + 1] == 'h' && (spelling[start] == 'z' || spelling[start] == 'c' || spelling[start] == 's');
        internal static bool IsInput(char value) => value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' || value == '\'' || value == '’' || value == ':' || "üÜāáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜńňǹḿ".IndexOf(value) >= 0 || char.GetUnicodeCategory(value) == UnicodeCategory.NonSpacingMark;

        internal sealed class Input
        {
            internal readonly string Raw, Key;
            internal readonly int[] Ends;
            internal readonly HashSet<int> Boundaries = new HashSet<int>();
            internal Input(string raw)
            {
                Raw = raw;
                var key = new StringBuilder(raw.Length); var ends = new List<int>();
                for (int offset = 0; offset < raw.Length; offset++)
                {
                    char value = raw[offset];
                    if (value == '\'' || value == '’' || char.IsWhiteSpace(value)) { Boundaries.Add(key.Length); continue; }
                    if (value == '\u0308' && key.Length > 0 && key[key.Length - 1] == 'u') { key[key.Length - 1] = 'v'; ends[ends.Count - 1] = offset + 1; continue; }
                    if (char.GetUnicodeCategory(value) == UnicodeCategory.NonSpacingMark) { if (ends.Count > 0) ends[ends.Count - 1] = offset + 1; continue; }
                    if ((value == 'u' || value == 'U') && offset + 1 < raw.Length && raw[offset + 1] == ':') { key.Append('v'); ends.Add(offset + 2); offset++; continue; }
                    if (value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z') { key.Append(char.ToLowerInvariant(value)); ends.Add(offset + 1); continue; }
                    string folded = char.IsSurrogate(value) ? value.ToString() : value.ToString().ToLowerInvariant().Normalize(NormalizationForm.FormD).Replace("u\u0308", "v");
                    foreach (char part in folded.Where(part => char.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)) { key.Append(part); ends.Add(offset + 1); }
                }
                Key = key.ToString(); Ends = ends.ToArray();
            }
            internal int Consumed(int keyLength)
            {
                int end = keyLength == 0 ? 0 : Ends[keyLength - 1];
                while (end < Raw.Length && (Raw[end] == '\'' || Raw[end] == '’' || char.IsWhiteSpace(Raw[end]))) end++;
                return end;
            }
        }
    }
}

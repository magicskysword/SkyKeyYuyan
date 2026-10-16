using System.Collections.Generic;
using System.Text;

namespace SkyKeyYuyan.Languages.Korean
{
    /// <summary>
    /// 从两套式键位、兼容字母或现代字母组合韩文音节。
    /// </summary>
    public static class HangulComposer
    {
        private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        private const string Vowels = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        private const string Finals = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
        private const string Keys = "rRseEfaqQtTdwWczxvgkoiOjpuPhynbml";
        private const string KeyValues = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎㅏㅐㅑㅒㅓㅔㅕㅖㅗㅛㅜㅠㅡㅣ";
        private static readonly Dictionary<string, char> CompoundVowels = new Dictionary<string, char>
        { ["ㅗㅏ"] = 'ㅘ', ["ㅗㅐ"] = 'ㅙ', ["ㅗㅣ"] = 'ㅚ', ["ㅜㅓ"] = 'ㅝ', ["ㅜㅔ"] = 'ㅞ', ["ㅜㅣ"] = 'ㅟ', ["ㅡㅣ"] = 'ㅢ', ["ㅘㅣ"] = 'ㅙ', ["ㅝㅣ"] = 'ㅞ' };
        private static readonly Dictionary<string, char> CompoundFinals = new Dictionary<string, char>
        { ["ㄱㅅ"] = 'ㄳ', ["ㄴㅈ"] = 'ㄵ', ["ㄴㅎ"] = 'ㄶ', ["ㄹㄱ"] = 'ㄺ', ["ㄹㅁ"] = 'ㄻ', ["ㄹㅂ"] = 'ㄼ', ["ㄹㅅ"] = 'ㄽ', ["ㄹㅌ"] = 'ㄾ', ["ㄹㅍ"] = 'ㄿ', ["ㄹㅎ"] = 'ㅀ', ["ㅂㅅ"] = 'ㅄ' };

        /// <summary>
        /// 判断字符能否参与词内组合。
        /// </summary>
        public static bool IsInput(char value) => Keys.IndexOf(value) >= 0 || Keys.IndexOf(char.ToLowerInvariant(value)) >= 0 || Initials.IndexOf(value) >= 0 || Vowels.IndexOf(value) >= 0 || Finals.IndexOf(value) > 0 || (value >= '\u1100' && value <= '\u1112') || (value >= '\u1161' && value <= '\u1175') || (value >= '\u11a8' && value <= '\u11c2') || (value >= '\uac00' && value <= '\ud7a3');

        /// <summary>
        /// 重放词内输入，按现代初声、中声和终声规则生成显示文本。
        /// </summary>
        public static string Compose(string raw)
        {
            var output = new StringBuilder(raw.Length);
            foreach (char input in raw)
            {
                char value = Map(input);
                int vowel = Vowels.IndexOf(value), initial = Initials.IndexOf(value), final = Finals.IndexOf(value);
                if (output.Length == 0) { output.Append(value); continue; }
                char previous = output[output.Length - 1];
                if (previous >= '\uac00' && previous <= '\ud7a3')
                {
                    int syllable = previous - 0xac00, lead = syllable / 588, middle = syllable % 588 / 28, tail = syllable % 28;
                    if (vowel >= 0)
                    {
                        if (tail == 0)
                        {
                            if (CompoundVowels.TryGetValue(Vowels[middle].ToString() + value, out char combined)) output[output.Length - 1] = Syllable(lead, Vowels.IndexOf(combined), 0);
                            else output.Append(value);
                        }
                        else
                        {
                            char moving = Finals[tail]; int retained = 0;
                            foreach (var pair in CompoundFinals) if (pair.Value == moving) { retained = Finals.IndexOf(pair.Key[0]); moving = pair.Key[1]; break; }
                            output[output.Length - 1] = Syllable(lead, middle, retained);
                            int movingInitial = Initials.IndexOf(moving);
                            if (movingInitial >= 0) output.Append(Syllable(movingInitial, vowel, 0)); else output.Append(value);
                        }
                        continue;
                    }
                    if (final > 0)
                    {
                        if (tail == 0) { output[output.Length - 1] = Syllable(lead, middle, final); continue; }
                        if (CompoundFinals.TryGetValue(Finals[tail].ToString() + value, out char combined)) { output[output.Length - 1] = Syllable(lead, middle, Finals.IndexOf(combined)); continue; }
                    }
                }
                int previousInitial = Initials.IndexOf(previous);
                if (previousInitial >= 0 && vowel >= 0) { output[output.Length - 1] = Syllable(previousInitial, vowel, 0); continue; }
                if (Vowels.IndexOf(previous) >= 0 && vowel >= 0 && CompoundVowels.TryGetValue(previous.ToString() + value, out char combinedVowel)) { output[output.Length - 1] = combinedVowel; continue; }
                output.Append(value);
            }
            return output.ToString();
        }
        private static char Syllable(int initial, int vowel, int final) => (char)(0xac00 + (initial * 21 + vowel) * 28 + final);
        private static char Map(char value)
        {
            int key = Keys.IndexOf(value);
            if (key < 0) key = Keys.IndexOf(char.ToLowerInvariant(value));
            if (key >= 0) return KeyValues[key];
            if (value >= '\u1100' && value <= '\u1112') return Initials[value - '\u1100'];
            if (value >= '\u1161' && value <= '\u1175') return Vowels[value - '\u1161'];
            if (value >= '\u11a8' && value <= '\u11c2') return Finals[value - '\u11a7'];
            return value;
        }
    }
}

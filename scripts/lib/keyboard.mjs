export function keyboardKeys(language) {
  const rows = language === 'ko' ? ['1234567890', 'ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔ', 'ㅁㄴㅇㄹㅎㅗㅓㅏㅣ', 'ㅋㅌㅊㅍㅠㅜㅡ'] : ['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm'];
  const shiftedKorean = new Map([['ㅂ', 'ㅃ'], ['ㅈ', 'ㅉ'], ['ㄷ', 'ㄸ'], ['ㄱ', 'ㄲ'], ['ㅅ', 'ㅆ'], ['ㅐ', 'ㅒ'], ['ㅔ', 'ㅖ']]);
  const keys = [];
  rows.forEach((row, rowIndex) => [...row].forEach((output, index) => {
    const column = index * 2 + (rowIndex === 2 ? 1 : rowIndex === 3 ? 4 : 0);
    const alternate = rowIndex === 0 ? '!@#$%^&*()'[index] : shiftedKorean.get(output) ?? output.toUpperCase();
    keys.push([`key-${rowIndex}-${index}`, output.toUpperCase(), output, alternate, rowIndex, column, 'input', 2]);
  }));
  const input = (id, output, alternate, row, column) => keys.push([id, output, output, alternate, row, column, 'input', 2]);
  const action = (id, label, row, column, span) => keys.push([id, label, id === 'space' ? ' ' : '', id === 'space' ? ' ' : '', row, column, id === 'space' ? 'input' : id, span]);
  input('minus', '-', '_', 0, 20); input('equals', '=', '+', 0, 22); action('backspace', '退格', 0, 24, 4);
  input('open-bracket', language === 'zh-Hans' ? '（' : '[', language === 'zh-Hans' ? '【' : '{', 1, 20);
  input('close-bracket', language === 'zh-Hans' ? '）' : ']', language === 'zh-Hans' ? '】' : '}', 1, 22); action('delete', '删除', 1, 24, 4);
  input('semicolon', language === 'zh-Hans' ? '；' : ';', language === 'zh-Hans' ? '：' : ':', 2, 19);
  input('quote', "'", '"', 2, 21); action('newline', '换行', 2, 23, 5);
  action('shift', 'Shift', 3, 0, 4);
  input('punctuation-0', language === 'zh-Hans' ? '，' : ',', '<', 3, 18);
  input('punctuation-1', language === 'zh-Hans' ? '。' : '.', '>', 3, 20);
  input('punctuation-2', language === 'zh-Hans' ? '、' : '/', language === 'zh-Hans' ? '？' : '?', 3, 22); action('clear', '清空', 3, 24, 4);
  action('language', '语言', 4, 0, 4); action('completion', '补全', 4, 4, 4); action('space', '空格', 4, 8, 12);
  action('left', '←', 4, 20, 2); action('up', '↑', 4, 22, 2); action('down', '↓', 4, 24, 2); action('right', '→', 4, 26, 2);
  action('selectall', '全选', 5, 0, 5); action('retry', '重试转换', 5, 5, 6); action('cancel', '取消', 5, 11, 8); action('finish', '完成', 5, 19, 9);
  return keys.sort((left, right) => left[4] - right[4] || left[5] - right[5]);
}

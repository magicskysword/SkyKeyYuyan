import assert from 'node:assert/strict';
import test from 'node:test';
import { keyboardKeys } from '../lib/keyboard.mjs';

for (const language of ['en', 'zh-Hans', 'ko']) test(`${language} 键盘保持完整字符与无重叠的功能区`, () => {
  const keys = keyboardKeys(language);
  assert.equal(keys.length, 61);
  assert.equal(new Set(keys.map(key => key[0])).size, keys.length);
  for (let row = 0; row < 6; row++) {
    const entries = keys.filter(key => key[4] === row);
    assert.equal(Math.max(...entries.map(key => key[5] + key[7])), 28);
    for (let index = 1; index < entries.length; index++) assert.ok(entries[index - 1][5] + entries[index - 1][7] <= entries[index][5]);
  }
  assert.equal(keys.find(key => key[0] === 'space')[7], 12);
  assert.equal(keys.find(key => key[0] === 'space')[2], ' ');
  const characters = keys.filter(key => /^key-[123]-/.test(key[0])).map(key => key[2]).join('');
  assert.equal(characters, language === 'ko' ? 'ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔㅁㄴㅇㄹㅎㅗㅓㅏㅣㅋㅌㅊㅍㅠㅜㅡ' : 'qwertyuiopasdfghjklzxcvbnm');
});

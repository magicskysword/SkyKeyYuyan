import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizePinyin, assignIdentities, buildChinese } from '../lib/lexicon.mjs';

test('拼音规范化保留元音和音节边界', () => {
  assert.equal(normalizePinyin('Lǜ sè'), 'lv se');
  assert.equal(normalizePinyin("xi'an"), 'xi an');
  assert.equal(normalizePinyin('nu:3'), 'nv');
});

test('已有词条身份在重新排序和增补后保持', () => {
  const words = [{ text: '你好', spelling: 'ni hao', weight: 1 }, { text: '很', spelling: 'hen', weight: 1 }];
  const first = assignIdentities('zh', words);
  const second = assignIdentities('zh', [...words].reverse().concat({ text: '啊', spelling: 'a', weight: 2 }), first.identities);
  for (const word of first.words) assert.equal(second.words.find(next => next.text === word.text).id, word.id);
  assert.equal(new Set(second.words.map(word => word.id)).size, 3);
});

test('读音构建保留多音关系并报告缺失读音', () => {
  const result = buildChinese('银行 20 n\n行 5 n\n未知 2 n\n', '银行: yín háng\n行为: xíng wéi\n', []);
  assert.equal(result.words.find(word => word.text === '银行').spelling, 'yin hang');
  assert.deepEqual(result.words.filter(word => word.text === '行').map(word => word.spelling).sort(), ['hang', 'xing']);
  assert.equal(result.report.missingCount, 1);
});

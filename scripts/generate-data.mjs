import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildChinese, supplements, assignIdentities, ordinal } from './lib/lexicon.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = path => readFile(resolve(root, path), 'utf8');
const manifest = JSON.parse(await read('data/manifests/sources.json'));
if (manifest.formatVersion !== 1) throw new Error('数据来源清单版本无效');
const generated = resolve(root, 'data/generated');
const reports = resolve(root, 'artifacts/Reports');
await mkdir(reports, { recursive: true });

const chinese = buildChinese(await read('data/sources/jieba/dict.txt'), await read('data/sources/phrase-pinyin-data/pinyin.txt'), supplements(await read('data/supplements/chinese.tsv')));
const english = new Map(JSON.parse(await read('data/sources/english-words/index.json')).filter(word => /^[a-z]+$/.test(word)).map(word => [word, { text: word, spelling: word, weight: 1 }]));
for (const word of supplements(await read('data/supplements/english.tsv'))) english.set(word.text, word);
const languageRecords = [
  { directory: 'chinese', language: 'zh-Hans', prefix: 'zh', words: chinese.words },
  { directory: 'english', language: 'en', prefix: 'en', words: [...english.values()] }
];
const report = { dataVersion: manifest.dataVersion, formatVersion: 1, chinese: chinese.report, languages: {} };

for (const language of languageRecords) {
  const mapFile = `data/manifests/${language.directory}-identities.tsv`;
  let previous = new Map();
  try { previous = new Map((await read(mapFile)).split(/\r?\n/).filter(Boolean).map(line => { const [id, text, spelling] = line.split('\t'); return [text + '\t' + spelling, id]; })); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const assigned = assignIdentities(language.prefix, language.words, previous);
  await mkdir(resolve(generated, language.directory), { recursive: true });
  const lines = [`SKY\t1\t${language.language}\t${manifest.dataVersion}`, ...assigned.words.map(word => `${word.id}\t${word.text}\t${word.spelling}\t${word.weight}`)];
  const resource = lines.join('\n') + '\n';
  await writeFile(resolve(generated, language.directory, 'lexicon.bytes'), resource);
  const identities = [...assigned.identities].sort((left, right) => ordinal(left[1], right[1])).map(([key, id]) => id + '\t' + key).join('\n') + '\n';
  await writeFile(resolve(root, mapFile), identities);
  report.languages[language.language] = { entries: assigned.words.length, bytes: Buffer.byteLength(resource), identityEntries: assigned.identities.size };
}

const qwerty = ['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm'];
const korean = ['1234567890', 'ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔ', 'ㅁㄴㅇㄹㅎㅗㅓㅏㅣ', 'ㅋㅌㅊㅍㅠㅜㅡ'];
const shiftedKorean = new Map([['ㅂ', 'ㅃ'], ['ㅈ', 'ㅉ'], ['ㄷ', 'ㄸ'], ['ㄱ', 'ㄲ'], ['ㅅ', 'ㅆ'], ['ㅐ', 'ㅒ'], ['ㅔ', 'ㅖ']]);
for (const [directory, language, rows] of [['chinese', 'zh-Hans', qwerty], ['english', 'en', qwerty], ['korean', 'ko', korean]]) {
  await mkdir(resolve(generated, directory), { recursive: true });
  const keys = [];
  rows.forEach((row, rowIndex) => [...row].forEach((output, column) => keys.push([`key-${rowIndex}-${column}`, output, output, shiftedKorean.get(output) ?? output.toUpperCase(), rowIndex, column, 'input'])));
  const punctuation = language === 'zh-Hans' ? '，。？！、：；（）’' : ',.?!:;()-\'';
  [...punctuation].forEach((output, column) => keys.push([`punctuation-${column}`, output, output, output, 4, column, 'input']));
  const functions = [['space', '空格', ' ', 'input'], ['shift', 'Shift', '', 'shift'], ['backspace', '退格', '', 'backspace'], ['newline', '换行', '', 'newline'], ['language', '语言', '', 'language'], ['finish', '完成', '', 'finish'], ['cancel', '取消', '', 'cancel']];
  functions.forEach(([id, label, output, action], column) => keys.push([id, label, output, output, 5, column, action]));
  ['left', 'right', 'up', 'down', 'delete', 'selectall', 'clear', 'completion', 'retry'].forEach((action, column) => keys.push([action, ({ left: '左移', right: '右移', up: '上移', down: '下移', delete: '删除', selectall: '全选', clear: '清空', completion: '补全', retry: '重试转换' })[action], '', '', 6, column, action]));
  await writeFile(resolve(generated, directory, 'keyboard.bytes'), [`SKYKEYS\t1\t${language}\t${manifest.dataVersion}`, ...keys.map(key => key.join('\t'))].join('\n') + '\n');
}

const ownLicense = (await read('LICENSE')).replaceAll('\r\n', '\n').split('\n\nThird-party data:')[0].trimEnd();
const licenseParts = new Map();
let license = ownLicense;
for (const source of manifest.sources.filter(source => source.id.endsWith('-license'))) {
  const part = '\n\nThird-party data: ' + source.id.replace(/-license$/, '') + '\nSource: ' + source.url + '\n\n' + (await read('data/sources/' + source.file)).replaceAll('\r\n', '\n').trimEnd();
  licenseParts.set(source.id, part);
  license += part;
}
await writeFile(resolve(root, 'LICENSE'), license + '\n');
const moduleLicenses = new Map([
  ['core', ownLicense], ['korean', ownLicense],
  ['chinese', ownLicense + ['jieba-license', 'phrase-pinyin-data-license', 'python-pinyin-license'].map(id => licenseParts.get(id)).join('')],
  ['english', ownLicense + licenseParts.get('english-words-license')]
]);
for (const [group, text] of moduleLicenses) {
  await mkdir(resolve(generated, 'licenses', group), { recursive: true });
  await writeFile(resolve(generated, 'licenses', group, 'LICENSE'), text + '\n');
}
for (const suffix of ['core', 'chinese', 'english', 'korean', 'unity', 'fairygui', 'ui.ugui', 'ui.fairygui']) {
  await writeFile(resolve(root, 'unity/SkyKeyYuyan.Unity/Packages/com.stellarnox.skykeyyuyan.' + suffix, 'LICENSE'), (moduleLicenses.get(suffix) ?? ownLicense) + '\n');
}
await writeFile(resolve(reports, 'language-data.json'), JSON.stringify(report, null, 2) + '\n');
process.stdout.write(JSON.stringify(report, null, 2) + '\n');

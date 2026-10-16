const han = /^\p{Script=Han}+$/u;
export const ordinal = (left, right) => left < right ? -1 : left > right ? 1 : 0;

export function normalizePinyin(spelling) {
  const normalized = spelling.toLowerCase().normalize('NFD').replaceAll('u\u0308', 'v').replaceAll('u:', 'v').replace(/\p{M}|[1-5]/gu, '').replace(/['’\s]+/gu, ' ').trim();
  if (!/^[a-z]+(?: [a-z]+)*$/.test(normalized)) throw new Error(`无效拼音读音: ${spelling}`);
  return normalized;
}

export function parsePhrases(text) {
  const phrases = new Map();
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith('#')) continue;
    const boundary = line.indexOf(':');
    if (boundary < 1) throw new Error('词语读音记录缺少分隔符');
    const word = line.slice(0, boundary).trim();
    if (!han.test(word)) continue;
    const spelling = normalizePinyin(line.slice(boundary + 1).split('#')[0].trim());
    if (spelling.split(' ').length !== [...word].length) continue;
    if (!phrases.has(word)) phrases.set(word, new Set());
    phrases.get(word).add(spelling);
  }
  return phrases;
}

export function supplements(text) {
  return text.split(/\r?\n/).filter(line => line && !line.startsWith('#')).map(line => {
    const [text, spelling, weight] = line.split('\t');
    if (!text || !spelling || !Number.isFinite(Number(weight)) || Number(weight) < 0) throw new Error('补充词条记录无效');
    return { text, spelling, weight: Number(weight) };
  });
}

export function buildChinese(jieba, phraseText, overrides) {
  const phrases = parsePhrases(phraseText);
  const characters = new Map();
  for (const [word, readings] of phrases) for (const reading of readings) {
    const syllables = reading.split(' ');
    [...word].forEach((character, index) => {
      if (!characters.has(character)) characters.set(character, new Set());
      characters.get(character).add(syllables[index]);
    });
  }
  for (const entry of overrides) if ([...entry.text].length === 1) {
    if (!characters.has(entry.text)) characters.set(entry.text, new Set());
    characters.get(entry.text).add(normalizePinyin(entry.spelling));
  }
  const result = new Map();
  const missing = [];
  let explicit = 0, inferred = 0, sourceWords = 0;
  const add = entry => result.set(entry.text + '\t' + entry.spelling, entry);
  for (const line of jieba.split(/\r?\n/)) {
    if (!line) continue;
    const [word, frequency] = line.split(' ');
    if (!han.test(word)) continue;
    const weight = Number(frequency);
    if (!Number.isFinite(weight) || weight < 0) throw new Error('基础词频无效');
    sourceWords++;
    let readings = phrases.get(word);
    if (!readings && [...word].length === 1) readings = characters.get(word);
    if (readings) explicit++;
    if (!readings && [...word].every(character => characters.get(character)?.size === 1)) {
      readings = new Set([[...word].map(character => [...characters.get(character)][0]).join(' ')]);
      inferred++;
    }
    if (!readings) { missing.push(word); continue; }
    for (const spelling of readings) add({ text: word, spelling, weight });
  }
  for (const entry of overrides) add({ ...entry, spelling: normalizePinyin(entry.spelling) });
  return { words: [...result.values()], report: { sourceWords, explicit, inferred, missingCount: missing.length, missingSample: missing.slice(0, 100) } };
}

export function assignIdentities(language, words, previous = new Map()) {
  const identities = new Map(previous);
  let next = 0;
  for (const id of identities.values()) next = Math.max(next, Number(id.slice(id.lastIndexOf('.') + 1)) || 0);
  const ordered = [...words].sort((left, right) => ordinal(left.text, right.text) || ordinal(left.spelling, right.spelling));
  const output = ordered.map(word => {
    const key = word.text + '\t' + word.spelling;
    if (!identities.has(key)) identities.set(key, language + '.' + String(++next).padStart(7, '0'));
    return { ...word, id: identities.get(key) };
  });
  return { words: output, identities };
}

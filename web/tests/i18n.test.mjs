import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import {
  createTranslator, pickLanguage, loadLanguage, t, getLanguage, getLocale,
  SUPPORTED_LANGUAGES, DEFAULT_LANGUAGE,
} from '../js/i18n.js';

const read = (relative) => readFileSync(new URL(relative, import.meta.url), 'utf8');
const it = JSON.parse(read('../i18n/it.json'));
const en = JSON.parse(read('../i18n/en.json'));

const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

test('Italian and English have exactly the same keys', () => {
  assert.deepEqual(Object.keys(en).sort(), Object.keys(it).sort());
});

test('no translation is empty', () => {
  for (const [name, messages] of Object.entries({ it, en })) {
    for (const [key, text] of Object.entries(messages)) {
      assert.ok(typeof text === 'string' && text.trim().length > 0, `${name}: "${key}" is empty`);
    }
  }
});

test('both languages use the same placeholders for every key', () => {
  for (const key of Object.keys(it)) {
    assert.deepEqual(placeholders(en[key]), placeholders(it[key]), `placeholders differ for "${key}"`);
  }
});

test('every plural key has both the _one and the _other form', () => {
  for (const [name, messages] of Object.entries({ it, en })) {
    for (const key of Object.keys(messages)) {
      const base = key.replace(/_(one|other)$/, '');
      if (base === key) continue;
      assert.ok(`${base}_one` in messages && `${base}_other` in messages, `${name}: "${base}" needs _one and _other`);
    }
  }
});

// Every key the code uses must exist, and every key must be used: no typos, no dead strings.
test('keys used by the pages and by index.html match the translation files', () => {
  const html = read('../index.html');
  const scripts = readdirSync(new URL('../js/', import.meta.url)).filter((f) => f.endsWith('.js'))
    .map((f) => read(`../js/${f}`)).join('\n');
  const sources = `${html}\n${scripts}`;

  const baseKeys = new Set(Object.keys(it).map((k) => k.replace(/_(one|other)$/, '')));
  const namespaces = new Set([...baseKeys].map((k) => k.split('.')[0]));
  const pattern = new RegExp(`['"]((?:${[...namespaces].join('|')})\\.[A-Za-z]+)['"]`, 'g');
  const used = new Set([...sources.matchAll(pattern)].map((m) => m[1]));
  // In index.html: data-i18n="key" and data-i18n-attr="aria-label:nav.main;placeholder:topbar.x".
  for (const m of html.matchAll(/data-i18n(?:-attr)?="([^"]+)"/g)) {
    for (const part of m[1].split(';')) used.add(part.split(':').pop().trim());
  }

  const unknown = [...used].filter((k) => !baseKeys.has(k));
  assert.deepEqual(unknown, [], `keys used in the code but missing from the translations: ${unknown.join(', ')}`);

  const unused = [...baseKeys].filter((k) => !used.has(k));
  assert.deepEqual(unused, [], `translations that the code never uses: ${unused.join(', ')}`);
});

test('translator fills placeholders and leaves unknown ones visible', () => {
  const translate = createTranslator({ hello: 'Ciao {name}', two: '{a}/{b}' });
  assert.equal(translate('hello', { name: 'Anna' }), 'Ciao Anna');
  assert.equal(translate('two', { a: 1 }), '1/{b}');
});

test('translator picks the plural form from count', () => {
  const messages = { 'n_one': '{count} film', 'n_other': '{count} film' };
  const english = createTranslator({ n_one: '{count} cinema', n_other: '{count} cinemas' }, {}, 'en');
  assert.equal(english('n', { count: 1 }), '1 cinema');
  assert.equal(english('n', { count: 3 }), '3 cinemas');
  assert.equal(english('n', { count: 0 }), '0 cinemas');
  assert.equal(createTranslator(messages, {}, 'it')('n', { count: 1 }), '1 film');
});

test('italian plural rules work for large numbers (the "many" category falls back to _other)', () => {
  const translate = createTranslator({ n_one: '{count} cosa', n_other: '{count} cose' }, {}, 'it');
  assert.equal(translate('n', { count: 1_000_000 }), '1000000 cose');
});

test('a missing key falls back to the default language, then shows the key', () => {
  const translate = createTranslator({}, { only: 'solo italiano' }, 'en');
  assert.equal(translate('only'), 'solo italiano');
  assert.equal(translate('nothing.here'), 'nothing.here');
});

test('pickLanguage prefers the stored choice, then the browser, then Italian', () => {
  assert.equal(pickLanguage('en', ['it-IT']), 'en');
  assert.equal(pickLanguage(null, ['fr-FR', 'en-GB', 'it']), 'en');
  assert.equal(pickLanguage('xx', ['de']), DEFAULT_LANGUAGE);
  assert.equal(pickLanguage(undefined), 'it');
  assert.deepEqual(SUPPORTED_LANGUAGES, ['it', 'en']);
});

test('loadLanguage switches the current language and locale', async () => {
  const loader = async (language) => (language === 'en' ? en : it);
  await loadLanguage('en', loader);
  assert.equal(getLanguage(), 'en');
  assert.equal(getLocale(), 'en-GB');
  assert.equal(t('nav.films'), 'Films');
  assert.equal(t('films.count', { count: 2 }), '2 films');
  await loadLanguage('it', loader);
  assert.equal(t('nav.films'), 'Film');
  assert.equal(t('cinema.showingsCount', { count: 1 }), '1 proiezione');
  assert.equal(t('cinema.showingsCount', { count: 4 }), '4 proiezioni');
});

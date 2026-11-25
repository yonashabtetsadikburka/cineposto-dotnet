// Translations. Every user-facing string lives in i18n/<language>.json (Italian is the default, English the
// secondary language). This module has no DOM access except applyTranslations(), so it can be tested in Node.

export const SUPPORTED_LANGUAGES = ['it', 'en'];
export const DEFAULT_LANGUAGE = 'it';
const LOCALES = { it: 'it-IT', en: 'en-GB' };

/** Picks the language: a stored choice first, then the browser preferences, then Italian. */
export function pickLanguage(stored, preferred = []) {
  if (SUPPORTED_LANGUAGES.includes(stored)) return stored;
  for (const tag of preferred) {
    const base = String(tag).toLowerCase().split('-')[0];
    if (SUPPORTED_LANGUAGES.includes(base)) return base;
  }
  return DEFAULT_LANGUAGE;
}

/**
 * Builds a translate function. A key can have plural variants (key_one, key_other, ...): they are chosen when
 * the parameters contain a numeric `count`. A key missing from `messages` is looked up in `fallback`, then the
 * key itself is returned, so a missing translation is visible instead of blank.
 */
export function createTranslator(messages, fallback = {}, language = DEFAULT_LANGUAGE) {
  const rules = new Intl.PluralRules(LOCALES[language] || language);

  const find = (source, key, params) => {
    if (params && typeof params.count === 'number') {
      const category = rules.select(params.count);
      return source[`${key}_${category}`] ?? source[`${key}_other`] ?? source[key];
    }
    return source[key];
  };

  return (key, params) => {
    const text = find(messages, key, params) ?? find(fallback, key, params) ?? key;
    return text.replace(/\{(\w+)\}/g, (placeholder, name) => (
      params && name in params ? String(params[name]) : placeholder));
  };
}

let language = DEFAULT_LANGUAGE;
let translate = createTranslator({}, {}, DEFAULT_LANGUAGE);

export function t(key, params) {
  return translate(key, params);
}

export function getLanguage() {
  return language;
}

export function getLocale() {
  return LOCALES[language];
}

/** Loads a language with the given loader (lang) => Promise<messages> and makes it the current one. */
export async function loadLanguage(requested, loadMessages) {
  const messages = await loadMessages(requested);
  const fallback = requested === DEFAULT_LANGUAGE ? {} : await loadMessages(DEFAULT_LANGUAGE);
  language = requested;
  translate = createTranslator(messages, fallback, requested);
}

/**
 * Fills the static texts of the page: data-i18n="key" sets the text, data-i18n-attr="attribute:key;other:key"
 * sets attributes. Safe to call again after a language change.
 */
export function applyTranslations(root = document) {
  root.querySelectorAll('[data-i18n]').forEach((element) => {
    element.textContent = t(element.dataset.i18n);
  });
  root.querySelectorAll('[data-i18n-attr]').forEach((element) => {
    for (const pair of element.dataset.i18nAttr.split(';')) {
      const [attribute, key] = pair.split(':').map((part) => part.trim());
      if (attribute && key) element.setAttribute(attribute, t(key));
    }
  });
  if (root === document) {
    document.documentElement.lang = language;
    document.title = t('app.title');
  }
}

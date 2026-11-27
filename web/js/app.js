// Hash-router SPA: #/ #/films #/film?id= #/cerca #/mappa #/cinema. One index.html, no server-side routes.
import { renderHome } from './home.js';
import { renderFilm } from './film.js';
import { renderCerca } from './cerca.js';
import { renderMappa } from './mappa.js';
import { renderCinema } from './cinema.js';
import { renderFilms } from './films.js';
import { loadCatalog } from './data.js';
import { searchFilms } from './search.js';
import { t, getLanguage, getLocale, loadLanguage, pickLanguage, applyTranslations } from './i18n.js';
import { esc, ICONS } from './util.js';

const view = document.getElementById('view');
const LANGUAGE_KEY = 'cineposto-language';
const THEME_KEY = 'cineposto-theme';

// ---- Shared states (loading, error, empty). Text comes from the translations, never from here. ----
function showLoading(target, message = t('state.loading')) {
  target.innerHTML = `<div class="state"><div class="spinner" role="status" aria-label="${esc(t('state.loadingLabel'))}"></div><p>${esc(message)}</p></div>`;
}
function showError(target, error, retry) {
  const message = t(error?.key ?? 'error.unexpected');
  target.innerHTML = `<div class="state"><p class="state-title">${esc(t('error.title'))}</p><p class="state-sub">${esc(message)}</p>${retry ? `<button class="btn primary" data-retry>${esc(t('state.retry'))}</button>` : ''}</div>`;
  target.querySelector('[data-retry]')?.addEventListener('click', retry);
}
function showEmpty(target) {
  target.innerHTML = `<div class="state"><p class="state-title">${esc(t('state.emptyTitle'))}</p><p class="state-sub">${esc(t('state.emptySubtitle'))}</p></div>`;
}
function showNoData(target, retry) {
  target.innerHTML = `<div class="state"><p class="state-title">${esc(t('state.noData'))}</p>${retry ? `<button class="btn primary" data-retry>${esc(t('state.retry'))}</button>` : ''}</div>`;
  target.querySelector('[data-retry]')?.addEventListener('click', retry);
}
const helpers = { showLoading, showError, showEmpty, showNoData };

function setActive(route) {
  document.querySelectorAll('[data-route]').forEach((a) => a.classList.toggle('active', a.dataset.route === route));
}

// ---- Router ----
let lastHash = '#/';
let openFiltersPending = false;

async function route() {
  view._cleanup?.();
  view._cleanup = null;
  const raw = (location.hash || '#/').slice(1);
  const [path, qs] = raw.split('?');
  const query = new URLSearchParams(qs || '');
  if (path === '/film') {
    // Remember where the user came from, for the back button of the film page.
    try {
      const previous = lastHash.slice(1).split('?')[0];
      sessionStorage.setItem('cineposto-back', (previous && previous !== '/film') ? lastHash : '#/');
    } catch { /* storage is optional */ }
  }
  try {
    if (path === '/films') { setActive('films'); await renderFilms(view, helpers); }
    else if (path === '/film') { setActive('home'); await renderFilm(view, query, helpers); }
    else if (path === '/cerca') { setActive('cerca'); await renderCerca(view, query, helpers); }
    else if (path === '/mappa') { setActive('mappa'); await renderMappa(view, helpers); }
    else if (path === '/cinema') { setActive('cinema'); await renderCinema(view, query, helpers); }
    else { setActive('home'); await renderHome(view, helpers); }
  } catch (e) {
    showError(view, e, route);
  }
  if (openFiltersPending) {
    openFiltersPending = false;
    document.getElementById('filtersDialog').showModal();
  }
  lastHash = location.hash || '#/';
  window.scrollTo({ top: 0 });
}

// ---- Theme (light or dark, saved in localStorage) ----
const themeButton = document.getElementById('themeToggle');
const currentTheme = () => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark');
function paintTheme() {
  const light = currentTheme() === 'light';
  themeButton.innerHTML = light ? ICONS.sun : ICONS.moon;
  themeButton.setAttribute('aria-pressed', String(light));
  // The browser UI (mobile address bar) takes the color of the header.
  const headerColor = getComputedStyle(document.documentElement).getPropertyValue('--topbar-bg').trim();
  if (headerColor) document.querySelector('meta[name="theme-color"]').setAttribute('content', headerColor);
}
themeButton.addEventListener('click', () => {
  const next = currentTheme() === 'light' ? 'dark' : 'light';
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem(THEME_KEY, next); } catch { /* storage is optional */ }
  paintTheme();
});

// ---- Language (Italian is the default; the choice is saved in localStorage) ----
async function fetchMessages(language) {
  const response = await fetch(`i18n/${language}.json`);
  if (!response.ok) throw new Error(`Cannot load i18n/${language}.json (${response.status})`);
  return response.json();
}
function storedLanguage() {
  try { return localStorage.getItem(LANGUAGE_KEY); } catch { return null; }
}
async function switchLanguage(language) {
  await loadLanguage(language, fetchMessages);
  try { localStorage.setItem(LANGUAGE_KEY, language); } catch { /* storage is optional */ }
  applyTranslations();
  paintTheme();
}
document.getElementById('langToggle').addEventListener('click', async () => {
  await switchLanguage(getLanguage() === 'it' ? 'en' : 'it');
  closeLive();
  liveBox.innerHTML = '';
  await route();
});

// ---- Filters (the sheet lives in index.html; outside the home it first goes there) ----
document.getElementById('openFilters').addEventListener('click', () => {
  const path = (location.hash || '#/').slice(1).split('?')[0];
  if (path === '/' || path === '') {
    document.getElementById('filtersDialog').showModal();
  } else {
    openFiltersPending = true;
    location.hash = '#/';
  }
});

// ---- Search box in the header ----
const liveInput = document.getElementById('liveQ');
const liveBox = document.getElementById('liveResults');
let liveTimer = null;
let liveItems = [];
let liveIndex = -1;

function closeLive() {
  liveBox.hidden = true;
  liveInput.setAttribute('aria-expanded', 'false');
  liveIndex = -1;
}
function paintLiveActive() {
  liveBox.querySelectorAll('.live-item').forEach((el, i) => el.classList.toggle('active', i === liveIndex));
}
async function runLive() {
  const text = liveInput.value.trim();
  if (text.length < 2) { closeLive(); liveBox.innerHTML = ''; return; }
  try {
    const catalog = await loadCatalog();
    liveItems = searchFilms(catalog.films, text, getLocale(), 6);
    liveIndex = -1;
    liveBox.innerHTML = liveItems.length ? liveItems.map((f, i) => `
      <a class="live-item" role="option" data-i="${i}" href="#/film?id=${f.id}">
        ${f.poster ? `<img src="${esc(f.poster)}" alt="" data-initial="${esc(f.title[0])}" loading="lazy" />` : `<div class="live-ph">${esc(f.title[0])}</div>`}
        <span><strong>${esc(f.title)}</strong><br /><small>${esc(f.durationMinutes ? t('film.minutes', { n: f.durationMinutes }) : '')}</small></span>
      </a>`).join('') : `<p class="live-empty">${esc(t('search.noResults'))}</p>`;
  } catch (e) {
    liveBox.innerHTML = `<p class="live-empty">${esc(t(e?.key ?? 'error.unexpected'))}</p>`;
  }
  liveBox.hidden = false;
  liveInput.setAttribute('aria-expanded', 'true');
}
liveInput.addEventListener('input', () => {
  clearTimeout(liveTimer);
  liveTimer = setTimeout(runLive, 250);
});
liveInput.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') { closeLive(); liveInput.blur(); }
  else if (e.key === 'Enter') {
    e.preventDefault();
    if (liveIndex >= 0 && liveItems[liveIndex]) location.hash = `#/film?id=${liveItems[liveIndex].id}`;
    else if (liveInput.value.trim().length >= 2) location.hash = `#/cerca?q=${encodeURIComponent(liveInput.value.trim())}`;
    closeLive();
  } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    if (!liveItems.length) return;
    e.preventDefault();
    liveIndex = e.key === 'ArrowDown' ? (liveIndex + 1) % liveItems.length : (liveIndex - 1 + liveItems.length) % liveItems.length;
    paintLiveActive();
  }
});
document.addEventListener('click', (e) => {
  if (!liveBox.hidden && !e.target.closest('.tb-search')) closeLive();
});

// ---- Images ----
// Posters and backdrops come from the data source, so an address can be broken. A failed poster is replaced by
// the placeholder with the initial of the title, and a failed background is simply removed.
document.addEventListener('error', (event) => {
  const img = event.target;
  if (!(img instanceof HTMLImageElement)) return;
  const initial = img.dataset.initial || '?';
  const replaceWith = (className) => {
    const placeholder = document.createElement('div');
    placeholder.className = className;
    placeholder.setAttribute('aria-hidden', 'true');
    placeholder.textContent = initial;
    img.replaceWith(placeholder);
  };
  if (img.classList.contains('poster')) replaceWith('placeholder');
  else if (img.classList.contains('result-poster')) replaceWith('result-poster ph');
  else if (img.closest('.live-item')) replaceWith('live-ph');
  else if (img.matches('.hero-bg, .hero-fg')) img.remove();
}, true);

// ---- Start ----
async function start() {
  try {
    await loadLanguage(pickLanguage(storedLanguage(), navigator.languages), fetchMessages);
  } catch {
    // Without translations the keys are shown as they are, which is visible and still usable.
  }
  applyTranslations();
  paintTheme();
  window.addEventListener('hashchange', route);
  // replaceState does not fire hashchange, so the first page is not rendered twice.
  if (!location.hash) history.replaceState(null, '', '#/');
  await route();
}
start();

// Search page. The search runs in the browser over the films already loaded: no request per keystroke.
import { loadCatalog } from './data.js';
import { searchFilms, MIN_QUERY_LENGTH } from './search.js';
import { t, getLocale } from './i18n.js';
import { esc, ICONS } from './util.js';

export async function renderCerca(view, query, { showLoading, showError }) {
  const initial = (query?.get('q') || '').trim();

  showLoading(view, t('state.loadingFilms'));
  let catalog;
  try {
    catalog = await loadCatalog();
  } catch (e) {
    showError(view, e, () => renderCerca(view, query, { showLoading, showError }));
    return;
  }

  view.innerHTML = `
    <div class="searchbar">
      <span class="search-icon" aria-hidden="true">${ICONS.search}</span>
      <input id="q" type="search" placeholder="${esc(t('topbar.searchPlaceholder'))}" autocomplete="off" aria-label="${esc(t('search.fieldLabel'))}" />
      <button id="clearQ" class="icon-btn" aria-label="${esc(t('search.clear'))}">${ICONS.x}</button>
    </div>
    <div id="results"><p class="state-sub">${esc(t('search.hint'))}</p></div>`;

  const input = view.querySelector('#q');
  const box = view.querySelector('#results');
  let timer = null;

  function run(text) {
    if (text.length < MIN_QUERY_LENGTH) {
      box.innerHTML = `<p class="state-sub">${esc(t('search.minChars'))}</p>`;
      return;
    }
    const films = searchFilms(catalog.films, text, getLocale());
    box.innerHTML = films.length ? films.map((f) => `
      <a class="result" href="#/film?id=${f.id}">
        ${f.poster ? `<img class="result-poster" src="${esc(f.poster)}" alt="" data-initial="${esc(f.title[0])}" loading="lazy" />` : `<div class="result-poster ph">${esc(f.title[0])}</div>`}
        <div class="result-info"><p class="result-title">${esc(f.title)}</p><p class="result-meta">${esc(f.durationMinutes ? t('film.minutes', { n: f.durationMinutes }) : '')}</p></div>
        <span class="result-arrow" aria-hidden="true">${ICONS.chevR}</span>
      </a>`).join('') : `<p class="state-sub">${esc(t('search.noResults'))}</p>`;
  }

  if (initial) {
    input.value = initial;
    run(initial);
  } else {
    input.focus();
  }

  input.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(() => run(input.value.trim()), 200);
  });
  view.querySelector('#clearQ').addEventListener('click', () => {
    input.value = ''; box.innerHTML = ''; input.focus();
  });
  view._cleanup = () => clearTimeout(timer);
}

// Films catalog: every film showing on the chosen day, with genre, city and cinema filters.
import { loadCatalog } from './data.js';
import { t, getLocale } from './i18n.js';
import { esc, ICONS } from './util.js';
import { filmCard } from './home.js';
import {
  weekDates, pillLabel, dayMonth, pickDefaultDate, todayStr, getSelectedDate, setSelectedDate,
} from './dates.js';

const SORTS = {
  title: (a, b) => a.title.localeCompare(b.title, getLocale()),
  duration: (a, b) => (b.durationMinutes || 0) - (a.durationMinutes || 0),
};
const SORT_LABEL_KEYS = { title: 'sort.title', duration: 'sort.duration' };

export async function renderFilms(view, { showLoading, showError, showEmpty, showNoData }) {
  const state = { date: getSelectedDate(), genre: '', city: '', cinema: '', sort: 'title' };

  async function load({ force = false } = {}) {
    showLoading(view, t('state.loadingFilms'));
    try {
      const catalog = await loadCatalog({ force });
      if (!state.date) {
        state.date = pickDefaultDate(catalog.dates, todayStr());
        setSelectedDate(state.date);
      }
      paint(catalog);
    } catch (e) {
      showError(view, e, () => load({ force: true }));
    }
  }

  function paint(catalog) {
    if (!catalog.dates.length) {
      showNoData(view, () => load({ force: true }));
      return;
    }

    // The chosen cinema has to be in the chosen city.
    if (state.cinema && state.city && catalog.cinema(state.cinema)?.city !== state.city) state.cinema = '';

    const onDay = catalog.filmsOn(state.date);
    const genres = [...new Set(onDay.flatMap((f) => f.genres))].sort((a, b) => a.localeCompare(b, getLocale()));
    const cities = [...catalog.cities].sort((a, b) => a.localeCompare(b, getLocale()));
    const cinemaOptions = state.city ? catalog.cinemas.filter((c) => c.city === state.city) : catalog.cinemas;
    const citySlugs = new Set(cinemaOptions.map((c) => c.slug));

    const filtered = onDay
      .filter((f) => (!state.genre || f.genres.includes(state.genre))
        && (!state.city || f.cinemaSlugs.some((s) => citySlugs.has(s)))
        && (!state.cinema || f.cinemaSlugs.includes(state.cinema)))
      .sort(SORTS[state.sort]);

    const week = weekDates();
    const dayPills = week.includes(state.date) ? week : [state.date, ...week];
    const chip = (name, text) => `<button class="filter-chip" data-clear="${name}">${esc(text)} <span aria-hidden="true">${ICONS.x}</span></button>`;
    const chips = [
      state.genre ? chip('genre', t('films.genreChip', { value: state.genre })) : '',
      state.city ? chip('city', t('films.cityChip', { value: state.city })) : '',
      state.cinema ? chip('cinema', catalog.cinema(state.cinema)?.name ?? state.cinema) : '',
    ].filter(Boolean).join('');
    const options = (items, selected) => items.map(([value, label]) => `<option value="${esc(value)}" ${value === selected ? 'selected' : ''}>${esc(label)}</option>`).join('');

    view.innerHTML = `
      <div class="carousel" aria-label="${esc(t('date.list'))}">
        <button class="car-arrow" data-scroll="#filmsDates" data-dir="-1" aria-label="${esc(t('date.previous'))}">${ICONS.chevL}</button>
        <div class="datebar" id="filmsDates" role="tablist">
          ${dayPills.map((d) => `<button class="pill ${d === state.date ? 'active' : ''}" data-date="${d}">${esc(week.includes(d) ? pillLabel(d, week.indexOf(d)) : dayMonth(d))}</button>`).join('')}
        </div>
        <button class="car-arrow" data-scroll="#filmsDates" data-dir="1" aria-label="${esc(t('date.next'))}">${ICONS.chevR}</button>
      </div>
      <div class="films-toolbar">
        <span class="films-count">${esc(t('films.count', { count: filtered.length }))}</span>
        <label>${esc(t('films.genre'))}
          <select id="fGenre"><option value="">${esc(t('filters.all'))}</option>${options(genres.map((g) => [g, g]), state.genre)}</select>
        </label>
        <label>${esc(t('films.city'))}
          <select id="fCity"><option value="">${esc(t('filters.allFeminine'))}</option>${options(cities.map((c) => [c, c]), state.city)}</select>
        </label>
        <label>${esc(t('films.cinema'))}
          <select id="fCinema"><option value="">${esc(t('filters.all'))}</option>${options(cinemaOptions.map((c) => [c.slug, c.name]), state.cinema)}</select>
        </label>
        <label>${esc(t('sort.label'))}
          <select id="fSort">${options(Object.keys(SORTS).map((k) => [k, t(SORT_LABEL_KEYS[k])]), state.sort)}</select>
        </label>
      </div>
      ${(state.genre || state.city || state.cinema) ? `<div class="active-filters films-chips">${chips}<button id="clearFilters" class="clear-all">${esc(t('filters.clearAll'))}</button></div>` : ''}
      ${filtered.length ? `<div class="films-grid">${filtered.map((f) => filmCard(f, {
    extra: `<p class="film-cinemas"><span>${esc(t('film.cinemasCount', { count: f.cinemaSlugs.length }))}</span></p>`,
  })).join('')}</div>` : ''}`;

    if (!filtered.length) {
      const empty = document.createElement('div');
      view.appendChild(empty);
      showEmpty(empty);
    }

    const repaint = () => paint(catalog);
    view.querySelectorAll('#filmsDates .pill').forEach((pill) => pill.addEventListener('click', () => {
      state.date = pill.dataset.date; setSelectedDate(state.date); repaint();
    }));
    view.querySelector('#fGenre').addEventListener('change', (e) => { state.genre = e.target.value; repaint(); });
    view.querySelector('#fCity').addEventListener('change', (e) => { state.city = e.target.value; repaint(); });
    view.querySelector('#fCinema').addEventListener('change', (e) => { state.cinema = e.target.value; repaint(); });
    view.querySelector('#fSort').addEventListener('change', (e) => { state.sort = e.target.value; repaint(); });
    view.querySelectorAll('.filter-chip').forEach((c) => c.addEventListener('click', () => { state[c.dataset.clear] = ''; repaint(); }));
    view.querySelector('#clearFilters')?.addEventListener('click', () => {
      Object.assign(state, { genre: '', city: '', cinema: '', sort: 'title' }); repaint();
    });
    view.querySelectorAll('.car-arrow').forEach((b) => b.addEventListener('click', () => {
      view.querySelector(b.dataset.scroll).scrollBy({ left: Number(b.dataset.dir) * 220, behavior: 'smooth' });
    }));
  }

  await load();
}

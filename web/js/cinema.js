// Cinema pages: the list of cinemas and the day-by-day showtimes of one cinema.
import { loadCatalog } from './data.js';
import { t, getLocale } from './i18n.js';
import { esc, ICONS, placeLine } from './util.js';
import { cinemaColor, cinemaLogo } from './cinema-theme.js';
import { dayLabel, todayStr } from './dates.js';

const SORT_LABEL_KEYS = { city: 'sort.city', name: 'sort.name', count: 'sort.count' };

function badge(cinema, large = false) {
  const logo = cinemaLogo(cinema.slug);
  if (logo) {
    return large
      ? `<img class="cinema-logo lg" src="${esc(logo)}" alt="${esc(t('cinema.logoAlt', { name: cinema.name }))}" />`
      : `<img class="cinema-badge" src="${esc(logo)}" alt="" loading="lazy" />`;
  }
  return `<span class="cinema-badge initial${large ? ' lg' : ''}" aria-hidden="true">${esc((cinema.name || '?')[0])}</span>`;
}

function cinemaCard(cinema, showingCount) {
  const empty = showingCount === 0;
  return `<a class="cinema-card cinema-link cinema-accent${empty ? ' is-empty' : ''}" style="--cinema-color:${cinemaColor(cinema.slug)}" href="#/cinema?slug=${encodeURIComponent(cinema.slug)}">
    ${badge(cinema)}
    <div><strong>${esc(cinema.name)}</strong><br />
    <span class="state-sub">${esc(placeLine(cinema))}</span><br />
    ${empty ? `<span class="cinema-empty">${esc(t('cinema.noShowings'))}</span>` : `<span class="state-sub">${esc(t('cinema.showingsCount', { count: showingCount }))}</span>`}</div>
    <span class="result-arrow" aria-hidden="true">${ICONS.chevR}</span>
  </a>`;
}

export async function renderCinema(view, query, helpers) {
  const slug = query.get('slug');
  return slug ? renderDetail(view, slug, helpers) : renderList(view, helpers);
}

async function renderList(view, { showLoading, showError, showNoData }) {
  const state = { sort: 'city' };

  async function load({ force = false } = {}) {
    showLoading(view, t('state.loadingCinemas'));
    try {
      paint(await loadCatalog({ force }));
    } catch (e) {
      showError(view, e, () => load({ force: true }));
    }
  }

  function ordered(catalog) {
    const locale = getLocale();
    const count = (c) => catalog.showingsOfCinema(c.slug).length;
    const byName = [...catalog.cinemas].sort((a, b) => a.name.localeCompare(b.name, locale));
    if (state.sort === 'name') return { flat: byName };
    if (state.sort === 'count') return { flat: [...catalog.cinemas].sort((a, b) => count(b) - count(a) || a.name.localeCompare(b.name, locale)) };
    // By city: cities A to Z, cinemas by name, cinemas without showings last in their city.
    const groups = new Map();
    for (const cinema of byName) {
      const city = cinema.city || t('cinema.otherCity');
      if (!groups.has(city)) groups.set(city, []);
      groups.get(city).push(cinema);
    }
    for (const list of groups.values()) list.sort((a, b) => ((count(a) === 0) - (count(b) === 0)) || a.name.localeCompare(b.name, locale));
    return { groups: [...groups.entries()].sort(([a], [b]) => a.localeCompare(b, locale)) };
  }

  function paint(catalog) {
    if (!catalog.cinemas.length) {
      showNoData(view, () => load({ force: true }));
      return;
    }
    const total = catalog.showings.length;
    const layout = ordered(catalog);
    const countOf = (c) => catalog.showingsOfCinema(c.slug).length;
    view.innerHTML = `
      <div class="films-toolbar">
        <span class="films-count">${esc(t('cinema.cinemasCount', { count: catalog.cinemas.length }))} · ${esc(t('cinema.showingsCount', { count: total }))}</span>
        <label>${esc(t('sort.label'))}
          <select id="cSort">${Object.entries(SORT_LABEL_KEYS).map(([k, key]) => `<option value="${k}" ${k === state.sort ? 'selected' : ''}>${esc(t(key))}</option>`).join('')}</select>
        </label>
      </div>
      ${layout.groups ? layout.groups.map(([city, list]) => `
        <section class="city-group">
          <div class="section-head"><h3>${esc(city)}</h3><span class="state-sub">${esc(t('cinema.cinemasCount', { count: list.length }))}</span></div>
          <div class="cinema-list">${list.map((c) => cinemaCard(c, countOf(c))).join('')}</div>
        </section>`).join('')
    : `<div class="cinema-list cinema-list-flat">${layout.flat.map((c) => cinemaCard(c, countOf(c))).join('')}</div>`}`;
    view.querySelector('#cSort').addEventListener('change', (e) => { state.sort = e.target.value; paint(catalog); });
  }

  await load();
}

async function renderDetail(view, slug, { showLoading, showError }) {
  showLoading(view, t('state.loadingSchedule'));
  try {
    const catalog = await loadCatalog();
    const cinema = catalog.cinema(slug);
    if (!cinema) {
      view.innerHTML = `<div class="state"><p class="state-title">${esc(t('cinema.notFound'))}</p><a class="btn primary" href="#/cinema">${esc(t('cinema.all'))}</a></div>`;
      return;
    }

    const showings = catalog.showingsOfCinema(slug);
    const dates = [...new Set(showings.map((s) => s.date))].sort();
    const today = todayStr();
    let selected = dates.includes(today) ? today : (dates.find((d) => d >= today) ?? dates[dates.length - 1]);

    view.innerHTML = `
      <a class="back-link" href="#/cinema">${ICONS.chevL} ${esc(t('cinema.all'))}</a>
      <div class="cinema-head" style="--cinema-color:${cinemaColor(slug)}">
        ${badge(cinema, true)}
        <div>
          <h1>${esc(cinema.name)}</h1>
          <p class="state-sub">${esc(placeLine(cinema))}</p>
          <div class="hero-actions">
            <a class="btn ghost" target="_blank" rel="noopener" href="https://www.google.com/maps/search/?api=1&query=${cinema.latitude},${cinema.longitude}">${esc(t('cinema.maps'))}</a>
            ${cinema.website ? `<a class="btn ghost" target="_blank" rel="noopener" href="${esc(cinema.website)}">${esc(t('cinema.website'))}</a>` : ''}
          </div>
        </div>
      </div>
      <div class="datebar" id="cinemaDates" role="tablist" aria-label="${esc(t('date.list'))}">
        ${dates.map((d) => `<button class="pill ${d === selected ? 'active' : ''}" data-date="${d}">${esc(dayLabel(d))}</button>`).join('')}
      </div>
      <div id="cinemaFilms"></div>`;

    const box = view.querySelector('#cinemaFilms');
    const paintFilms = () => {
      const perFilm = new Map();
      for (const showing of showings.filter((s) => s.date === selected)) {
        if (!perFilm.has(showing.filmId)) perFilm.set(showing.filmId, []);
        perFilm.get(showing.filmId).push(showing);
      }
      const groups = [...perFilm.entries()]
        .map(([filmId, list]) => ({ film: catalog.film(filmId), list }))
        .sort((a, b) => a.film.title.localeCompare(b.film.title, getLocale()));
      box.innerHTML = groups.length ? groups.map(({ film, list }) => {
        // A start time may appear in several showings (different rooms): list it once, linked to its ticket page.
        const chips = [];
        for (const showing of list) {
          for (const time of showing.times) {
            if (!chips.some((c) => c.time === time && c.url === showing.sourceUrl)) chips.push({ time, url: showing.sourceUrl });
          }
        }
        chips.sort((a, b) => a.time.localeCompare(b.time));
        const buy = list.find((s) => s.sourceUrl)?.sourceUrl;
        return `<div class="result">
          ${film.poster ? `<img class="result-poster" src="${esc(film.poster)}" alt="" data-initial="${esc(film.title[0])}" loading="lazy" />` : `<div class="result-poster ph">${esc(film.title[0])}</div>`}
          <div class="result-info">
            <a class="result-title" href="#/film?id=${film.id}">${esc(film.title)}</a>
            <div class="times-row">${chips.map(({ time, url }) => (url
    ? `<a class="time-chip" target="_blank" rel="noopener" href="${esc(url)}"><span class="time-text">${esc(time)}</span></a>`
    : `<span class="time-chip"><span class="time-text">${esc(time)}</span></span>`)).join('')}</div>
            ${buy ? `<a class="buy-link" target="_blank" rel="noopener" href="${esc(buy)}">${esc(t('film.buy'))} ${ICONS.arrowR}</a>` : ''}
          </div>
        </div>`;
      }).join('') : `<p class="empty-text">${esc(t('cinema.noShowingsOnDate'))}</p>`;
    };
    paintFilms();
    view.querySelectorAll('#cinemaDates .pill').forEach((pill) => pill.addEventListener('click', () => {
      selected = pill.dataset.date;
      view.querySelectorAll('#cinemaDates .pill').forEach((p) => p.classList.toggle('active', p === pill));
      paintFilms();
    }));
  } catch (e) {
    showError(view, e, () => renderDetail(view, slug, { showLoading, showError }));
  }
}

// Home: featured films, day bar, one row per cinema and the filters sheet.
import { loadCatalog } from './data.js';
import { t, getLocale } from './i18n.js';
import { esc, ICONS } from './util.js';
import { cinemaColor, cinemaLogo } from './cinema-theme.js';
import {
  weekDates, pillLabel, dayMonth, fullDate, pickDefaultDate, todayStr, getSelectedDate, setSelectedDate,
} from './dates.js';

const SORTS = {
  title: (a, b) => a.title.localeCompare(b.title, getLocale()),
  duration: (a, b) => (b.durationMinutes || 0) - (a.durationMinutes || 0),
};
const SORT_LABEL_KEYS = { title: 'sort.title', duration: 'sort.duration' };

/** A film card. `badge` is the start times line; `extra` is extra HTML (already escaped) shown under the card. */
export function filmCard(film, { badge = '', extra = '' } = {}) {
  const poster = film.poster
    ? `<img class="poster" src="${esc(film.poster)}" alt="${esc(t('film.posterAlt', { title: film.title }))}" data-initial="${esc((film.title || '?')[0])}" loading="lazy" />`
    : `<div class="placeholder" aria-hidden="true">${esc((film.title || '?')[0])}</div>`;
  const sub = film.durationMinutes ? t('film.minutes', { n: film.durationMinutes }) : '';
  return `<a class="card" href="#/film?id=${film.id}">${poster}<p class="title">${esc(film.title)}</p>`
    + `${sub ? `<p class="sub">${esc(sub)}</p>` : ''}${badge ? `<p class="times">${esc(badge)}</p>` : ''}${extra}</a>`;
}

// At most three start times and a "+N" counter for the rest.
function timesBadge(times) {
  if (!times.length) return '';
  const shown = times.slice(0, 3).join(' · ');
  return times.length > 3 ? `${shown} +${times.length - 3}` : shown;
}

let heroTimer = null;
function stopHero() {
  if (heroTimer) { clearInterval(heroTimer); heroTimer = null; }
}

export async function renderHome(view, { showLoading, showError, showEmpty, showNoData }) {
  const state = { date: null, staleDismissed: false, genre: '', cinema: '', sort: 'title' };

  const dialog = document.getElementById('filtersDialog');
  const sortList = document.getElementById('sortList');
  const genreList = document.getElementById('genreList');
  const cinemaList = document.getElementById('cinemaList');
  document.getElementById('filtersClose').onclick = () => dialog.close();
  dialog.onclick = (e) => { if (e.target === dialog) dialog.close(); };

  const sheetOption = (label, active) => `<button class="sheet-option ${active ? 'active' : ''}"><span>${esc(label)}</span>${active ? '<span class="check" aria-hidden="true">✓</span>' : ''}</button>`;

  async function load({ force = false } = {}) {
    stopHero();
    showLoading(view, t('state.loadingSchedule'));
    try {
      const catalog = await loadCatalog({ force });
      if (!state.date) {
        // The day chosen before (shared with the Films page), else today if it has showings, else the closest
        // day that has some.
        state.date = getSelectedDate() ?? pickDefaultDate(catalog.dates, todayStr());
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

    // A day in the past can only be shown when the data is out of date.
    const stale = state.date < todayStr();
    const all = catalog.filmsOn(state.date);
    const genres = [...new Set(all.flatMap((f) => f.genres))].sort((a, b) => a.localeCompare(b, getLocale()));

    sortList.innerHTML = Object.keys(SORTS).map((key) => `<li>${sheetOption(t(SORT_LABEL_KEYS[key]), state.sort === key)}</li>`).join('');
    genreList.innerHTML = `<li>${sheetOption(t('filters.allGenres'), !state.genre)}</li>`
      + genres.map((g) => `<li>${sheetOption(g, state.genre === g)}</li>`).join('');
    cinemaList.innerHTML = `<li>${sheetOption(t('filters.allCinemas'), !state.cinema)}</li>`
      + catalog.cinemas.map((c) => `<li>${sheetOption(c.name, state.cinema === c.slug)}</li>`).join('');
    [...sortList.children].forEach((li, i) => { li.firstChild.onclick = () => { state.sort = Object.keys(SORTS)[i]; dialog.close(); paint(catalog); }; });
    [...genreList.children].forEach((li, i) => { li.firstChild.onclick = () => { state.genre = i === 0 ? '' : genres[i - 1]; dialog.close(); paint(catalog); }; });
    [...cinemaList.children].forEach((li, i) => { li.firstChild.onclick = () => { state.cinema = i === 0 ? '' : catalog.cinemas[i - 1].slug; dialog.close(); paint(catalog); }; });

    const filtered = all
      .filter((f) => (!state.genre || f.genres.includes(state.genre)) && (!state.cinema || f.cinemaSlugs.includes(state.cinema)))
      .sort(SORTS[state.sort]);

    const week = weekDates();
    const dayPills = (week.includes(state.date) ? week : [state.date, ...week]);
    const chips = [
      state.genre ? `<button class="filter-chip" data-clear="genre">${esc(state.genre)} <span aria-hidden="true">${ICONS.x}</span></button>` : '',
      state.cinema ? `<button class="filter-chip" data-clear="cinema">${esc(catalog.cinema(state.cinema)?.name ?? state.cinema)} <span aria-hidden="true">${ICONS.x}</span></button>` : '',
    ].filter(Boolean).join('');

    const featured = filtered.slice(0, 6);
    const sections = catalog.cinemas.map((cinema) => ({
      cinema,
      films: filtered.filter((f) => f.cinemaSlugs.includes(cinema.slug)),
      next: catalog.dates.find((d) => d >= state.date && catalog.showingsOfCinema(cinema.slug).some((s) => s.date === d)) ?? null,
    }));

    const arrow = (label, target, dir) => `<button class="car-arrow" data-scroll="${target}" data-dir="${dir}" aria-label="${esc(label)}">${dir < 0 ? ICONS.chevL : ICONS.chevR}</button>`;
    const row = (id, cards) => `<div class="carousel">${arrow(t('carousel.back'), `#${id}`, -1)}<div class="row" id="${id}">${cards}</div>${arrow(t('carousel.forward'), `#${id}`, 1)}</div>`;

    view.innerHTML = `
      ${(state.genre || state.cinema) ? `<div class="active-filters">${chips}<button id="clearFilters" class="clear-all">${esc(t('filters.clearAll'))}</button></div>` : ''}
      ${stale && !state.staleDismissed ? `<div class="stale-banner" role="status"><span>${ICONS.warn} ${esc(t('date.stale', { date: fullDate(state.date) }))}</span><button id="staleClose" aria-label="${esc(t('date.staleClose'))}">${ICONS.x}</button></div>` : ''}
      ${filtered.length ? `<section class="hero" aria-label="${esc(t('home.featured'))}">
        <div class="hero-track" id="heroTrack">
          ${featured.map((f) => `
            <div class="hero-slide ${f.backdrop ? '' : 'poster-only'}">
              ${(f.backdrop || f.poster) ? `<img class="hero-bg" src="${esc(f.backdrop || f.poster)}" alt="" aria-hidden="true" loading="lazy" />` : ''}
              <div class="hero-body">
                ${!f.backdrop && f.poster ? `<img class="hero-fg" src="${esc(f.poster)}" alt="${esc(t('film.posterAlt', { title: f.title }))}" loading="lazy" />` : ''}
                <div class="hero-text">
                  <h2>${esc(f.title)}</h2>
                  <p class="hero-genres">${esc(f.genres.slice(0, 3).join(' · '))}</p>
                  <p class="hero-cinemas">${esc(f.cinemaSlugs.map((s) => catalog.cinema(s)?.name ?? s).join(' · '))}</p>
                  ${f.description ? `<p class="hero-overview">${esc(f.description.slice(0, 110))}${f.description.length > 110 ? '…' : ''}</p>` : ''}
                  <div class="hero-actions">
                    <a class="btn play" href="#/film?id=${f.id}">${ICONS.play} ${esc(t('home.details'))}</a>
                    <a class="btn info" href="#/film?id=${f.id}">${esc(t('home.showtimes'))}</a>
                    <a class="btn trailer" target="_blank" rel="noopener" href="https://www.youtube.com/results?search_query=${encodeURIComponent(`${f.title} trailer`)}">${esc(t('home.trailer'))}</a>
                  </div>
                </div>
              </div>
            </div>`).join('')}
        </div>
        <div class="hero-dots" id="heroDots">${featured.map((_, i) => `<button aria-label="${esc(t('home.slide', { n: i + 1 }))}" data-i="${i}" class="${i === 0 ? 'active' : ''}"></button>`).join('')}</div>
      </section>` : ''}
      <div class="carousel" aria-label="${esc(t('date.list'))}">
        ${arrow(t('date.previous'), '#dateStrip', -1)}
        <div class="datebar" id="dateStrip" role="tablist">
          ${dayPills.map((d) => `<button class="pill ${d === state.date ? 'active' : ''}" data-date="${d}">${esc(week.includes(d) ? pillLabel(d, week.indexOf(d)) : dayMonth(d))}</button>`).join('')}
        </div>
        ${arrow(t('date.next'), '#dateStrip', 1)}
      </div>
      <div class="rows">
        ${filtered.length ? `<section class="section">
          <div class="section-head"><h3>${esc(t('home.nowShowing'))}</h3><a class="show-all-link" href="#/films">${esc(t('home.showAll'))} ${ICONS.arrowR}</a></div>
          ${row('nowRow', filtered.map((f) => filmCard(f, { badge: timesBadge(f.times) })).join(''))}
        </section>` : ''}
        ${sections.map(({ cinema, films, next }) => {
    const logo = cinemaLogo(cinema.slug);
    return `<section class="section cinema-section" style="--cinema-color:${cinemaColor(cinema.slug)}">
          <div class="section-head">${logo ? `<img class="cinema-badge" src="${esc(logo)}" alt="" loading="lazy" />` : `<span class="cinema-badge initial" aria-hidden="true">${esc(cinema.name[0])}</span>`}<h3>${esc(cinema.name)}</h3><a class="show-all-link" href="#/cinema?slug=${encodeURIComponent(cinema.slug)}">${esc(t('home.showAll'))} ${ICONS.arrowR}</a></div>
          ${films.length ? row(`row-${cinema.slug}`, films.map((f) => filmCard(f)).join('')) : `<p class="empty-text">${esc(t('home.noShowings', { date: dayMonth(state.date) }))}${next ? ` <button class="buy-link linklike" data-goto="${next}">${esc(t('home.seeDate', { date: dayMonth(next) }))} ${ICONS.arrowR}</button>` : ` ${esc(t('home.nothingScheduled'))}`}</p>`}
        </section>`;
  }).join('')}
      </div>`;

    if (!filtered.length) {
      const empty = document.createElement('div');
      view.querySelector('.rows').before(empty);
      showEmpty(empty);
    }

    // The hero advances every 5 seconds and stops as soon as the user touches it.
    const track = view.querySelector('#heroTrack');
    const dots = [...view.querySelectorAll('#heroDots button')];
    let index = 0;
    stopHero();
    if (track && featured.length > 1) {
      heroTimer = setInterval(() => {
        index = (index + 1) % featured.length;
        track.scrollTo({ left: index * track.clientWidth, behavior: 'smooth' });
        dots.forEach((d, i) => d.classList.toggle('active', i === index));
      }, 5000);
      track.addEventListener('pointerdown', stopHero, { once: true });
    }
    dots.forEach((dot) => dot.addEventListener('click', () => {
      index = Number(dot.dataset.i);
      track.scrollTo({ left: index * track.clientWidth, behavior: 'smooth' });
      dots.forEach((d, i) => d.classList.toggle('active', i === index));
    }));

    const chooseDate = (date) => {
      state.date = date; setSelectedDate(date); paint(catalog);
    };
    view.querySelectorAll('.pill').forEach((pill) => pill.addEventListener('click', () => chooseDate(pill.dataset.date)));
    view.querySelectorAll('[data-goto]').forEach((b) => b.addEventListener('click', () => chooseDate(b.dataset.goto)));
    view.querySelector('#staleClose')?.addEventListener('click', () => {
      state.staleDismissed = true;
      view.querySelector('.stale-banner')?.remove();
    });
    view.querySelectorAll('.car-arrow').forEach((b) => b.addEventListener('click', () => {
      const target = view.querySelector(b.dataset.scroll);
      const step = target.classList.contains('row') ? target.clientWidth : 220;
      target.scrollBy({ left: Number(b.dataset.dir) * step, behavior: 'smooth' });
    }));
    view.querySelectorAll('.filter-chip').forEach((chip) => chip.addEventListener('click', () => {
      state[chip.dataset.clear] = ''; paint(catalog);
    }));
    view.querySelector('#clearFilters')?.addEventListener('click', () => {
      Object.assign(state, { genre: '', cinema: '', sort: 'title' }); paint(catalog);
    });
  }

  view._cleanup = stopHero;
  await load();
}

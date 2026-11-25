// Film detail: backdrop, plot, trailer link and the showtimes of the film, cinema by cinema.
import { loadCatalog } from './data.js';
import { t } from './i18n.js';
import { esc, ICONS } from './util.js';
import { cinemaColor, cinemaLogo, normLang, splitScreen } from './cinema-theme.js';
import { dayLabel, todayStr } from './dates.js';

const PLOT_PREVIEW_LENGTH = 120;

// Where the "back" button goes: the page the user came from (stored by the router), or the home.
function backTarget() {
  try {
    const back = sessionStorage.getItem('cineposto-back');
    if (back && back.startsWith('#/')) return back;
  } catch { /* storage is optional */ }
  return '#/';
}

export async function renderFilm(view, query, { showLoading, showError }) {
  const id = query.get('id');
  if (!id) {
    view.innerHTML = `<div class="state"><p class="state-title">${esc(t('film.notSpecified'))}</p><a class="btn primary" href="#/">${esc(t('film.backHome'))}</a></div>`;
    return;
  }

  showLoading(view, t('state.loadingFilms'));
  try {
    const catalog = await loadCatalog();
    const film = catalog.film(id);
    if (!film) {
      view.innerHTML = `<div class="state"><p class="state-title">${esc(t('film.notFound'))}</p><a class="btn primary" href="#/">${esc(t('film.backHome'))}</a></div>`;
      return;
    }

    const byDate = new Map();
    for (const showing of catalog.showingsOfFilm(film.id)) {
      if (!byDate.has(showing.date)) byDate.set(showing.date, []);
      byDate.get(showing.date).push(showing);
    }
    const dates = [...byDate.keys()].sort();
    const today = todayStr();
    // Today if the film is on today, else its next day, else its last one.
    let selected = dates.includes(today) ? today : (dates.find((d) => d >= today) ?? dates[dates.length - 1]);
    const longPlot = (film.description || '').length > PLOT_PREVIEW_LENGTH;
    const image = film.backdrop || film.poster;

    view.innerHTML = `
      <section class="detail-hero">
        ${image ? `<img class="hero-bg" src="${esc(image)}" alt="" aria-hidden="true" /><img class="hero-fg" src="${esc(film.poster || film.backdrop)}" alt="${esc(t('film.posterAlt', { title: film.title }))}" />` : '<div class="detail-hero-ph"></div>'}
        <a class="back-btn" href="${esc(backTarget())}" aria-label="${esc(t('film.backLabel'))}">${ICONS.chevL}<span>${esc(t('film.back'))}</span></a>
      </section>
      <div class="detail-body">
        <div class="detail-head-info">
          <h1>${esc(film.title)}</h1>
          ${film.originalTitle && film.originalTitle !== film.title ? `<p class="meta-line">${esc(film.originalTitle)}</p>` : ''}
          ${film.durationMinutes ? `<p class="meta-line">${esc(t('film.minutes', { n: film.durationMinutes }))}</p>` : ''}
          ${film.director ? `<p class="meta-line">${esc(t('film.directedBy', { name: film.director }))}</p>` : ''}
        </div>
        ${film.genres.length ? `<p class="detail-genres">${esc(film.genres.join(', '))}</p>` : ''}
        ${film.description ? `<div class="detail-section"><h2>${esc(t('film.plot'))}</h2><p class="overview ${longPlot ? 'collapsed' : ''}" id="plot">${esc(film.description)}</p>${longPlot ? `<button class="read-more" id="morePlot">${esc(t('film.readMore'))}</button>` : ''}</div>` : ''}
        <a class="trailer-button" target="_blank" rel="noopener" href="https://www.youtube.com/results?search_query=${encodeURIComponent(`${film.title} trailer`)}">${ICONS.play} ${esc(t('film.watchTrailer'))}</a>
        <div class="detail-section"><h2>${esc(t('film.showtimes'))}</h2>
        <div class="datebar" id="filmDates">${dates.map((d) => `<button class="pill ${d === selected ? 'active' : ''} ${d === today ? 'today' : ''}" data-date="${d}">${esc(dayLabel(d))}</button>`).join('') || `<p class="state-sub">${esc(t('film.noShowings'))}</p>`}</div>
        <div id="filmTimes"></div></div>
      </div>`;

    const plot = view.querySelector('#plot');
    view.querySelector('#morePlot')?.addEventListener('click', (e) => {
      const collapsed = plot.classList.toggle('collapsed');
      e.target.textContent = t(collapsed ? 'film.readMore' : 'film.showLess');
    });

    const timesBox = view.querySelector('#filmTimes');
    const paintTimes = () => {
      const list = byDate.get(selected) ?? [];
      const nameOf = (slug) => catalog.cinema(slug)?.name ?? slug;
      // With many cinemas, a row of chips jumps to each cinema card.
      const jump = list.length > 5
        ? `<div class="cinema-chips" aria-label="${esc(t('film.goToCinema'))}">${list.map((s) => `<button class="chip-cinema" data-target="show-${esc(s.cinemaSlug)}" style="--cinema-color:${cinemaColor(s.cinemaSlug)}">${esc(nameOf(s.cinemaSlug))}</button>`).join('')}</div>`
        : '';
      timesBox.innerHTML = jump + (list.length ? list.map((s) => {
        const { format, room } = splitScreen(s.screen);
        const tags = [normLang(s.language), format].filter(Boolean).map((tag) => `<span class="show-tag">${esc(tag)}</span>`).join('');
        const times = s.times.map((time) => {
          const inner = `<span class="time-text">${esc(time)}</span>${room ? `<span class="time-detail">${esc(room)}</span>` : ''}`;
          return s.sourceUrl
            ? `<a class="time-chip" target="_blank" rel="noopener" href="${esc(s.sourceUrl)}">${inner}</a>`
            : `<span class="time-chip">${inner}</span>`;
        }).join('');
        const name = nameOf(s.cinemaSlug);
        const logo = cinemaLogo(s.cinemaSlug);
        const badge = logo
          ? `<img class="cinema-badge" src="${esc(logo)}" alt="" loading="lazy" />`
          : `<span class="cinema-badge initial" style="--cinema-color:${cinemaColor(s.cinemaSlug)}" aria-hidden="true">${esc(name[0])}</span>`;
        return `<div class="show-card cinema-accent" id="show-${esc(s.cinemaSlug)}" style="--cinema-color:${cinemaColor(s.cinemaSlug)}"><div class="show-head">${badge}<strong>${esc(name)}</strong>${tags}</div><div class="times-row">${times}</div>${s.sourceUrl ? `<a class="btn primary buy-cta" target="_blank" rel="noopener" href="${esc(s.sourceUrl)}">${esc(t('film.buy'))} ${ICONS.arrowR}</a>` : ''}</div>`;
      }).join('') : `<p class="empty-text">${esc(t('film.noTimesForDate'))}</p>`);
      timesBox.querySelectorAll('.chip-cinema').forEach((b) => b.addEventListener('click', () => {
        timesBox.querySelector(`#${CSS.escape(b.dataset.target)}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }));
    };
    paintTimes();
    view.querySelectorAll('#filmDates .pill').forEach((pill) => pill.addEventListener('click', () => {
      selected = pill.dataset.date;
      view.querySelectorAll('#filmDates .pill').forEach((p) => p.classList.toggle('active', p === pill));
      paintTimes();
    }));
  } catch (e) {
    showError(view, e, () => renderFilm(view, query, { showLoading, showError }));
  }
}

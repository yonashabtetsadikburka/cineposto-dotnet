// The catalog: cinemas, films and showings, loaded once and indexed in the browser. The data set is a few
// hundred showings, so every page filters locally instead of asking the API again.
import { api } from './api.js';

const CACHE_MS = 5 * 60 * 1000;

function groupBy(items, keyOf) {
  const map = new Map();
  for (const item of items) {
    const key = keyOf(item);
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(item);
  }
  return map;
}

/** Builds the indexes. Showings that point to an unknown film or cinema are ignored. */
export function buildCatalog(cinemas, films, showings) {
  const cinemaBySlug = new Map(cinemas.map((c) => [c.slug, c]));
  const filmById = new Map(films.map((f) => [f.id, f]));
  const valid = showings.filter((s) => filmById.has(s.filmId) && cinemaBySlug.has(s.cinemaSlug));

  const byFilm = groupBy(valid, (s) => s.filmId);
  const byCinema = groupBy(valid, (s) => s.cinemaSlug);
  const byDate = groupBy(valid, (s) => s.date);

  return {
    cinemas,
    films,
    showings: valid,
    dates: [...byDate.keys()].sort(),
    cities: [...new Set(cinemas.map((c) => c.city).filter(Boolean))],
    cinema: (slug) => cinemaBySlug.get(slug),
    film: (id) => filmById.get(Number(id)),
    showingsOfFilm: (id) => byFilm.get(Number(id)) ?? [],
    showingsOfCinema: (slug) => byCinema.get(slug) ?? [],

    /** Films shown on a day, each with the cinemas and the start times of that day. */
    filmsOn(date) {
      const perFilm = groupBy(byDate.get(date) ?? [], (s) => s.filmId);
      return [...perFilm.entries()].map(([filmId, list]) => ({
        ...filmById.get(filmId),
        cinemaSlugs: [...new Set(list.map((s) => s.cinemaSlug))],
        times: [...new Set(list.flatMap((s) => s.times))].sort(),
      }));
    },
  };
}

let cached = null;

/** Loads the catalog. Pass { force: true } to bypass the five-minute cache, for example on "Try again". */
export async function loadCatalog({ force = false } = {}) {
  if (!force && cached && Date.now() - cached.at < CACHE_MS) return cached.catalog;
  const [cinemas, films, showings] = await Promise.all([api.getCinemas(), api.getFilms(), api.getShowings()]);
  const catalog = buildCatalog(cinemas, films, showings);
  cached = { at: Date.now(), catalog };
  return catalog;
}

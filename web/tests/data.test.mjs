import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildCatalog } from '../js/data.js';
import { createApi, ApiError } from '../js/api.js';
import { searchFilms, fold } from '../js/search.js';
import { placeLine } from '../js/util.js';
import { normLang, splitScreen, cinemaColor, cinemaLogo, CINEMA_FALLBACK_COLOR } from '../js/cinema-theme.js';

// Same shape as the API answers (camelCase), with the values of the saved fixtures.
const cinemas = [
  { slug: 'clarici-foligno', name: 'Multiplex Politeama Clarici', city: 'Foligno', address: 'Via Garibaldi 137', region: 'Umbria', latitude: 42.95, longitude: 12.7, website: null },
  { slug: 'altro', name: 'Altro Cinema', city: 'Perugia', address: 'Via X', region: 'Umbria', latitude: 43.1, longitude: 12.4, website: null },
];
const films = [
  { id: 1, title: 'Nebbia sul Lago', titleNormalized: 'Nebbia sul Lago', originalTitle: null, poster: null, backdrop: null, description: null, director: null, durationMinutes: 100, genres: ['Fantasia'] },
  { id: 2, title: 'Festa Sintetica', titleNormalized: 'Festa Sintetica', originalTitle: 'Synthetic Party', poster: null, backdrop: null, description: null, director: null, durationMinutes: null, genres: [] },
];
const showings = [
  { filmId: 1, cinemaSlug: 'clarici-foligno', date: '2026-09-22', times: ['20:00', '18:30'], screen: null, language: 'ITA', sourceUrl: null, sessionAttributes: [] },
  { filmId: 1, cinemaSlug: 'altro', date: '2026-09-22', times: ['21:00', '20:00'], screen: null, language: null, sourceUrl: null, sessionAttributes: [] },
  { filmId: 2, cinemaSlug: 'clarici-foligno', date: '2026-09-25', times: ['20:00'], screen: null, language: 'ITA', sourceUrl: null, sessionAttributes: [] },
  { filmId: 99, cinemaSlug: 'clarici-foligno', date: '2026-09-26', times: ['20:00'], screen: null, language: null, sourceUrl: null, sessionAttributes: [] },
  { filmId: 1, cinemaSlug: 'ghost', date: '2026-09-27', times: ['20:00'], screen: null, language: null, sourceUrl: null, sessionAttributes: [] },
];

test('buildCatalog ignores showings of unknown films or cinemas', () => {
  const catalog = buildCatalog(cinemas, films, showings);
  assert.equal(catalog.showings.length, 3);
  assert.deepEqual(catalog.dates, ['2026-09-22', '2026-09-25']);
});

test('buildCatalog looks films and cinemas up, by number or text id', () => {
  const catalog = buildCatalog(cinemas, films, showings);
  assert.equal(catalog.film('2').title, 'Festa Sintetica');
  assert.equal(catalog.film(1).title, 'Nebbia sul Lago');
  assert.equal(catalog.film('nope'), undefined);
  assert.equal(catalog.cinema('altro').city, 'Perugia');
  assert.deepEqual(catalog.cities.sort(), ['Foligno', 'Perugia']);
});

test('buildCatalog groups showings by film and by cinema', () => {
  const catalog = buildCatalog(cinemas, films, showings);
  assert.equal(catalog.showingsOfFilm(1).length, 2);
  assert.equal(catalog.showingsOfFilm(404).length, 0);
  assert.equal(catalog.showingsOfCinema('clarici-foligno').length, 2);
});

test('filmsOn lists the films of a day with their cinemas and sorted, unique start times', () => {
  const catalog = buildCatalog(cinemas, films, showings);
  const day = catalog.filmsOn('2026-09-22');
  assert.equal(day.length, 1);
  assert.equal(day[0].title, 'Nebbia sul Lago');
  assert.deepEqual(day[0].cinemaSlugs.sort(), ['altro', 'clarici-foligno']);
  assert.deepEqual(day[0].times, ['18:30', '20:00', '21:00']);
  assert.deepEqual(catalog.filmsOn('2030-01-01'), []);
});

test('an empty catalog is valid', () => {
  const catalog = buildCatalog([], [], []);
  assert.deepEqual(catalog.dates, []);
  assert.deepEqual(catalog.filmsOn('2026-09-22'), []);
});

// ---- API client ----
const respond = (body, status = 200) => async () => ({ ok: status < 400, status, json: async () => body });

test('the API client calls the relative paths of the three endpoints', async () => {
  const seen = [];
  const api = createApi(async (url, options) => { seen.push([url, options.headers.Accept]); return { ok: true, json: async () => [] }; });
  await api.getCinemas();
  await api.getFilms();
  await api.getShowings();
  assert.deepEqual(seen, [
    ['api/v1/cinemas', 'application/json'],
    ['api/v1/films', 'application/json'],
    ['api/v1/showings', 'application/json'],
  ]);
});

test('the API client turns every failure into an ApiError that carries a translation key', async () => {
  await assert.rejects(createApi(respond({}, 500)).getFilms(), (e) => e instanceof ApiError && e.key === 'error.films');
  await assert.rejects(createApi(async () => { throw new TypeError('network'); }).getCinemas(), (e) => e.key === 'error.cinemas');
  await assert.rejects(
    createApi(async () => ({ ok: true, json: async () => { throw new SyntaxError('bad json'); } })).getShowings(),
    (e) => e.key === 'error.showings',
  );
});

// ---- Search ----
test('fold ignores case, accents and punctuation', () => {
  assert.equal(fold('  Così  è, la Vita! '), 'cosi e la vita');
});

test('searchFilms needs two characters and matches every word, in title or original title', () => {
  assert.deepEqual(searchFilms(films, 'n'), []);
  assert.deepEqual(searchFilms(films, 'nebbia lago').map((f) => f.id), [1]);
  assert.deepEqual(searchFilms(films, 'NEBBIA').map((f) => f.id), [1]);
  assert.deepEqual(searchFilms(films, 'synthetic').map((f) => f.id), [2]);
  assert.deepEqual(searchFilms(films, 'nebbia sintetica'), []);
});

test('searchFilms sorts by title and honours the limit', () => {
  const two = [films[0], { ...films[0], id: 3, title: 'Altra Nebbia' }];
  assert.deepEqual(searchFilms(two, 'nebbia', 'it').map((f) => f.title), ['Altra Nebbia', 'Nebbia sul Lago']);
  assert.equal(searchFilms(two, 'nebbia', 'it', 1).length, 1);
});

// ---- Cinema presentation ----
test('placeLine adds the city only when the address does not contain it', () => {
  assert.equal(placeLine({ address: 'Via Garibaldi 137, 06034 Foligno PG', city: 'Foligno' }), 'Via Garibaldi 137, 06034 Foligno PG');
  assert.equal(placeLine({ address: 'Via Roma 1', city: 'Perugia' }), 'Via Roma 1, Perugia');
  assert.equal(placeLine({ address: '', city: 'Perugia' }), 'Perugia');
  assert.equal(placeLine({ address: 'Via Roma 1' }), 'Via Roma 1');
});

test('normLang gives a code or nothing', () => {
  assert.equal(normLang('ITA'), 'ITA');
  assert.equal(normLang(' italiano '), 'ITA');
  assert.equal(normLang('lingua originale'), 'ORIG');
  assert.equal(normLang('VOST'), 'VOST');
  assert.equal(normLang('Klingon'), null);
  assert.equal(normLang(null), null);
});

test('splitScreen tells a format from a room', () => {
  assert.deepEqual(splitScreen('IMAX 3D'), { format: 'IMAX', room: null });
  assert.deepEqual(splitScreen('Sala 2'), { format: null, room: 'Sala 2' });
  assert.deepEqual(splitScreen(null), { format: null, room: null });
});

test('an unknown cinema gets the fallback color and no logo', () => {
  assert.equal(cinemaColor('unknown'), CINEMA_FALLBACK_COLOR);
  assert.equal(cinemaColor('clarici-foligno'), '#FF5722');
  assert.equal(cinemaLogo('clarici-foligno'), null);
});

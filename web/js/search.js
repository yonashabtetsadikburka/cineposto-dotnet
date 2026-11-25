// Title search over the films already loaded. Accents and case are ignored ("e" finds "è").

export function fold(text) {
  return String(text ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

export const MIN_QUERY_LENGTH = 2;

/** Films whose title or original title contains every word of the query, ordered by title. */
export function searchFilms(films, query, locale = 'it', limit = 50) {
  const folded = fold(query);
  if (folded.length < MIN_QUERY_LENGTH) return [];
  const words = folded.split(' ');
  return films
    .filter((film) => {
      const haystack = `${fold(film.title)} ${fold(film.originalTitle)}`;
      return words.every((word) => haystack.includes(word));
    })
    .sort((a, b) => a.title.localeCompare(b.title, locale))
    .slice(0, limit);
}

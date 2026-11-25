// Client of the CinePosto API. The base path is relative, so the page and the API are on the same origin:
// with <base href="/"> it resolves to /api/v1/... (no CORS involved).
//
//   GET api/v1/cinemas
//   GET api/v1/films
//   GET api/v1/showings
//
// Errors carry a translation key, never text, so the UI decides what to show and in which language.

export class ApiError extends Error {
  constructor(key, cause) {
    super(key);
    this.name = 'ApiError';
    this.key = key;
    this.cause = cause;
  }
}

export function createApi(fetchFn, base = 'api/v1') {
  async function getJson(path, errorKey) {
    let response;
    try {
      response = await fetchFn(`${base}/${path}`, { headers: { Accept: 'application/json' } });
    } catch (cause) {
      throw new ApiError(errorKey, cause);
    }
    if (!response.ok) throw new ApiError(errorKey);
    try {
      return await response.json();
    } catch (cause) {
      throw new ApiError(errorKey, cause);
    }
  }

  return {
    getCinemas: () => getJson('cinemas', 'error.cinemas'),
    getFilms: () => getJson('films', 'error.films'),
    getShowings: () => getJson('showings', 'error.showings'),
  };
}

export const api = createApi((...args) => fetch(...args));

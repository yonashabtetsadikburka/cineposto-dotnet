// Presentation data of the cinemas: accent colors and logos, keyed by cinema slug. A cinema that is not listed
// here still works: it gets the fallback color and an initial instead of a logo.

export const CINEMA_LOGOS = {
  'the-space-corciano': 'assets/the-space.jpg',
  'uci-perugia': 'assets/uci.png',
  postmodernissimo: 'assets/post.jpg',
};

export const CINEMA_COLORS = {
  postmodernissimo: '#E50914',
  'the-space-corciano': '#1E90FF',
  'the-space-terni': '#1E90FF',
  'uci-perugia': '#FFA500',
  'melies-perugia': '#9B59B6',
  'carpine-magione': '#27AE60',
  'esperia-bastia': '#16A085',
  'frontone-perugia': '#7CB342',
  'zenith-perugia': '#E91E63',
  'politeama-terni': '#00BCD4',
  'clarici-foligno': '#FF5722',
};

export const CINEMA_FALLBACK_COLOR = '#E50914';

export function cinemaColor(slug) {
  return CINEMA_COLORS[slug] || CINEMA_FALLBACK_COLOR;
}

export function cinemaLogo(slug) {
  return CINEMA_LOGOS[slug] || null;
}

// Language codes shown as small tags: ITA, ENG, VOST, ORIG. Null when the source gives none or something
// unknown: no tag is better than an invented one.
const LANGUAGE_CODES = {
  ITA: 'ITA',
  ITALIANO: 'ITA',
  ENG: 'ENG',
  VOST: 'VOST',
  'LINGUA ORIGINALE': 'ORIG',
};

export function normLang(value) {
  if (!value) return null;
  return LANGUAGE_CODES[String(value).trim().toUpperCase()] || null;
}

// The screen field mixes a format (2D, 3D, IMAX...) and a room ("Sala 2"). This tells them apart.
export function splitScreen(value) {
  if (!value) return { format: null, room: null };
  const text = String(value).trim();
  const format = text.match(/^(2D|3D|2K|IMAX|4DX|SCREENX)\b/i);
  if (format) return { format: format[1].toUpperCase(), room: null };
  return { format: null, room: text };
}

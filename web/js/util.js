// Small helpers shared by the pages: HTML escaping and the inline SVG icons.

export function esc(value) {
  return String(value ?? '').replace(/[&<>"']/g, (c) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]));
}

/** Address line of a cinema. The city is added only when the address does not already contain it. */
export function placeLine(cinema) {
  const address = cinema.address || '';
  const city = cinema.city || '';
  if (!city || address.toLowerCase().includes(city.toLowerCase())) return address;
  return [address, city].filter(Boolean).join(', ');
}

function svg(path) {
  return `<svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="${path}"/></svg>`;
}

export const ICONS = {
  chevL: svg('M15.75 19.5L8.25 12l7.5-7.5'),
  chevR: svg('M8.25 4.5l7.5 7.5-7.5 7.5'),
  arrowR: svg('M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3'),
  play: svg('M5.25 5.653c0-.856.917-1.398 1.667-.986l11.54 6.348a1.125 1.125 0 010 1.971l-11.54 6.347a1.125 1.125 0 01-1.667-.985V5.653z'),
  warn: svg('M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z'),
  x: svg('M6 18L18 6M6 6l12 12'),
  search: svg('M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z'),
  moon: svg('M21.752 15.002A9.718 9.718 0 0118 15.75c-5.385 0-9.75-4.365-9.75-9.75 0-1.33.266-2.597.748-3.752A9.753 9.753 0 0012 21.75a9.753 9.753 0 009.752-6.748z'),
  sun: svg('M12 3v2.25m6.364.386l-1.591 1.591M21 12h-2.25m-.386 6.364l-1.591-1.591M12 18.75V21m-4.773-4.227l-1.591 1.591M5.25 12H3m4.227-4.773L5.636 5.636M15.75 12a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0z'),
};

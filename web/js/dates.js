// Date helpers. Dates travel as ISO strings (yyyy-MM-dd), exactly as the API sends them. Day and month names
// come from Intl, so no language-specific text is written here.
import { getLocale, t } from './i18n.js';

const pad = (n) => String(n).padStart(2, '0');

/** Local calendar day of a Date as yyyy-MM-dd. */
export function toIso(date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function todayStr(now = new Date()) {
  return toIso(now);
}

/** Today and the next days, as ISO strings. */
export function weekDates(now = new Date(), days = 7) {
  return Array.from({ length: days }, (_, i) => {
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() + i);
    return toIso(d);
  });
}

// Noon UTC keeps the calendar day the same in every time zone.
function parse(iso) {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d, 12));
}

/** Short weekday plus day number, such as "MAR 22". Three-letter weekdays stay distinct (MAR is not MER). */
export function dayLabel(iso, locale = getLocale()) {
  const date = parse(iso);
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' })
    .format(date).replace(/\.$/, '').toUpperCase();
  return `${weekday} ${date.getUTCDate()}`;
}

/** Day and short month, such as "22 set". */
export function dayMonth(iso, locale = getLocale()) {
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(parse(iso));
}

/** Numeric date with the order of the locale, such as "22/09/2026". */
export function fullDate(iso, locale = getLocale()) {
  return new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' })
    .format(parse(iso));
}

export function pillLabel(iso, index) {
  return index === 0 ? t('date.today') : dayLabel(iso);
}

/**
 * Chooses the day to open on: today if it has showings, otherwise the next day that has some, otherwise the
 * most recent one. A result in the past means the data is out of date.
 */
export function pickDefaultDate(dates, today) {
  if (!dates.length || dates.includes(today)) return today;
  return dates.find((d) => d > today) ?? dates[dates.length - 1];
}

// The day chosen by the user, shared between the Home and Films pages.
let selected = null;

export function getSelectedDate() {
  return selected;
}

export function setSelectedDate(iso) {
  selected = iso;
}

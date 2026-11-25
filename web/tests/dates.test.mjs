import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadLanguage } from '../js/i18n.js';
import {
  toIso, todayStr, weekDates, dayLabel, dayMonth, fullDate, pillLabel, pickDefaultDate,
  getSelectedDate, setSelectedDate,
} from '../js/dates.js';

const messages = (language) => JSON.parse(readFileSync(new URL(`../i18n/${language}.json`, import.meta.url), 'utf8'));
const loader = async (language) => messages(language);

test('toIso and todayStr use the local calendar day', () => {
  assert.equal(toIso(new Date(2026, 8, 5)), '2026-09-05');
  assert.equal(todayStr(new Date(2026, 10, 25, 23, 59)), '2026-11-25');
});

test('weekDates returns seven days starting today, across a month end', () => {
  assert.deepEqual(weekDates(new Date(2026, 10, 28)), [
    '2026-11-28', '2026-11-29', '2026-11-30', '2026-12-01', '2026-12-02', '2026-12-03', '2026-12-04',
  ]);
  assert.equal(weekDates(new Date(2026, 0, 1), 3).length, 3);
});

test('dayLabel gives a three-letter weekday and the day, in the chosen language', () => {
  // 2026-09-22 is a Tuesday and 2026-09-23 a Wednesday.
  assert.equal(dayLabel('2026-09-22', 'it-IT'), 'MAR 22');
  assert.equal(dayLabel('2026-09-23', 'it-IT'), 'MER 23');
  assert.match(dayLabel('2026-09-22', 'en-GB'), /^TUE 22$/);
  assert.notEqual(dayLabel('2026-09-22', 'it-IT').split(' ')[0], dayLabel('2026-09-23', 'it-IT').split(' ')[0]);
});

test('dayMonth and fullDate follow the locale and do not shift with the time zone', () => {
  assert.match(dayMonth('2026-09-22', 'it-IT'), /^22 set/i);
  assert.match(dayMonth('2026-09-22', 'en-GB'), /^22 Sep/);
  assert.equal(fullDate('2026-09-22', 'it-IT'), '22/09/2026');
  assert.equal(fullDate('2026-01-01', 'en-GB'), '01/01/2026');
});

test('pillLabel says "today" for the first day and a weekday for the others', async () => {
  await loadLanguage('it', loader);
  assert.equal(pillLabel('2026-09-22', 0), 'Oggi');
  assert.match(pillLabel('2026-09-23', 1), /^MER 23$/);
  await loadLanguage('en', loader);
  assert.equal(pillLabel('2026-09-22', 0), 'Today');
  await loadLanguage('it', loader);
});

test('pickDefaultDate: today when it has showings', () => {
  assert.equal(pickDefaultDate(['2026-11-24', '2026-11-25', '2026-11-27'], '2026-11-25'), '2026-11-25');
});

test('pickDefaultDate: the next day with showings when today has none', () => {
  assert.equal(pickDefaultDate(['2026-11-20', '2026-11-27', '2026-11-28'], '2026-11-25'), '2026-11-27');
});

test('pickDefaultDate: the latest past day when there is nothing ahead (the data is out of date)', () => {
  assert.equal(pickDefaultDate(['2026-09-22', '2026-09-25'], '2026-11-25'), '2026-09-25');
});

test('pickDefaultDate: with no data at all it stays on today', () => {
  assert.equal(pickDefaultDate([], '2026-11-25'), '2026-11-25');
});

test('the selected day is shared and starts unset', () => {
  assert.equal(getSelectedDate(), null);
  setSelectedDate('2026-09-22');
  assert.equal(getSelectedDate(), '2026-09-22');
  setSelectedDate(null);
});

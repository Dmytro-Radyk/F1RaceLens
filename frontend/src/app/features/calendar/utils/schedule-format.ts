import type { SessionSchedule } from '../models/calendar.types';

type ScheduledDate = Pick<SessionSchedule, 'date' | 'startsAt'>;
const dateFormat = new Intl.DateTimeFormat('uk-UA', { day: 'numeric', month: 'long' });
const weekdayFormat = new Intl.DateTimeFormat('uk-UA', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
});
const timeFormat = new Intl.DateTimeFormat('uk-UA', {
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
});

function displayDate(schedule: ScheduledDate): Date {
  if (schedule.startsAt) return new Date(schedule.startsAt);
  const [year, month, day] = schedule.date.split('-').map(Number);
  return new Date(year, month - 1, day, 12);
}

export function formatScheduleDate(schedule: ScheduledDate): string {
  return dateFormat.format(displayDate(schedule));
}
export function formatSessionDay(schedule: ScheduledDate): string {
  return weekdayFormat.format(displayDate(schedule));
}
export function formatScheduleTime(schedule: ScheduledDate): string {
  return schedule.startsAt ? timeFormat.format(new Date(schedule.startsAt)) : 'Час не вказано';
}

export function isPastSchedule(schedule: ScheduledDate, now: number): boolean {
  if (schedule.startsAt) return Date.parse(schedule.startsAt) < now;
  const today = new Date(now);
  const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  return schedule.date < date;
}

export function scheduleLabel(schedule: ScheduledDate, now: number): string {
  if (displayDate(schedule).toDateString() === new Date(now).toDateString()) return 'Сьогодні';
  return isPastSchedule(schedule, now) ? 'Минулий етап' : 'Попереду';
}

const countryNames: Readonly<Record<string, string>> = {
  Argentina: 'Аргентина',
  Australia: 'Австралія',
  Austria: 'Австрія',
  Azerbaijan: 'Азербайджан',
  Bahrain: 'Бахрейн',
  Belgium: 'Бельгія',
  Brazil: 'Бразилія',
  Canada: 'Канада',
  China: 'Китай',
  France: 'Франція',
  Germany: 'Німеччина',
  Hungary: 'Угорщина',
  India: 'Індія',
  Italy: 'Італія',
  Japan: 'Японія',
  Malaysia: 'Малайзія',
  Mexico: 'Мексика',
  Monaco: 'Монако',
  Morocco: 'Марокко',
  Netherlands: 'Нідерланди',
  Portugal: 'Португалія',
  Qatar: 'Катар',
  'Saudi Arabia': 'Саудівська Аравія',
  Singapore: 'Сінгапур',
  'South Africa': 'ПАР',
  'South Korea': 'Південна Корея',
  Spain: 'Іспанія',
  Sweden: 'Швеція',
  Switzerland: 'Швейцарія',
  Turkey: 'Туреччина',
  UAE: 'ОАЕ',
  UK: 'Велика Британія',
  USA: 'США',
  'United States': 'США',
  'United Kingdom': 'Велика Британія',
};
export function countryName(name: string): string {
  return countryNames[name] ?? name;
}
export const userTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export function formatUpdatedAt(value: string): string {
  return new Intl.DateTimeFormat('uk-UA', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import type { CalendarResponse, RaceResponse, RaceSchedule } from './models/calendar.types';
import { formatScheduleDate, formatScheduleTime, isPastSchedule } from './utils/schedule-format';

const futureRace: RaceSchedule = {
  season: 2026,
  round: 23,
  name: 'Abu Dhabi Grand Prix',
  circuit: { id: 'yas_marina', name: 'Yas Marina Circuit', locality: 'Abu Dhabi', country: 'UAE' },
  date: '2026-12-06',
  startsAt: '2026-12-06T13:00:00Z',
  sessions: [
    { kind: 'FirstPractice', name: 'Практика 1', date: '2026-12-04', startsAt: null },
    { kind: 'Race', name: 'Гонка', date: '2026-12-06', startsAt: '2026-12-06T13:00:00Z' },
  ],
};
const pastRace: RaceSchedule = {
  ...futureRace,
  round: 1,
  name: 'Australian Grand Prix',
  date: '2026-03-08',
  startsAt: '2026-03-08T04:00:00Z',
};
const calendar: CalendarResponse = {
  season: 2026,
  updatedAt: '2026-10-17T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  races: [pastRace, futureRace],
};

describe('Calendar routes', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.UTC(2026, 9, 17, 12));
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  async function settle(harness: RouterTestingHarness, text: string) {
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain(text);
    });
  }

  it('allows guest access, identifies the next race and filters the calendar', async () => {
    const harness = await RouterTestingHarness.create('/calendar');
    http.expectOne('/api/seasons/current/calendar').flush(calendar);
    await settle(harness, 'Abu Dhabi Grand Prix');
    const page = harness.routeNativeElement!;
    expect(page.querySelector('.next-race h2')?.textContent).toContain(futureRace.name);
    expect(page.querySelectorAll('.race-card')).toHaveLength(2);
    const upcoming = [...page.querySelectorAll<HTMLButtonElement>('.calendar-filters button')].find(
      (button) => button.textContent?.trim() === 'Майбутні',
    )!;
    upcoming.click();
    harness.detectChanges();
    expect(page.querySelectorAll('.race-card')).toHaveLength(1);
    expect(page.querySelector('.race-card')?.getAttribute('href')).toBe('/calendar/2026/23');
  });

  it('loads a Grand Prix directly and preserves missing session times', async () => {
    const harness = await RouterTestingHarness.create('/calendar/2026/23');
    const response: RaceResponse = { ...calendar, race: futureRace };
    http.expectOne('/api/seasons/2026/races/23').flush(response);
    await settle(harness, 'Розклад вікенду');
    const page = harness.routeNativeElement!;
    expect(page.querySelectorAll('.session-list li')).toHaveLength(2);
    expect(page.querySelector('.session-list time')?.textContent).toContain('Час не вказано');
    expect(page.querySelector('.back-link')?.getAttribute('href')).toBe('/calendar?season=2026');
  });

  it('opens an archive URL and returns from a race to the same season', async () => {
    const race: RaceSchedule = {
      ...pastRace,
      season: 1950,
      date: '1950-05-13',
      startsAt: null,
      sessions: [{ kind: 'Race', name: 'Гонка', date: '1950-05-13', startsAt: null }],
    };
    const archive = { ...calendar, season: 1950, races: [race] };
    const harness = await RouterTestingHarness.create('/calendar?season=1950');
    http.expectOne('/api/seasons/1950/calendar').flush(archive);
    await settle(harness, 'Сезон 1950');
    expect(harness.routeNativeElement!.querySelector<HTMLSelectElement>('select')!.value).toBe(
      '1950',
    );
    expect(harness.routeNativeElement!.querySelector('.next-race')).toBeNull();
    expect(harness.routeNativeElement!.querySelector('.race-card')?.getAttribute('href')).toBe(
      '/calendar/1950/1',
    );

    await harness.navigateByUrl('/calendar/1950/1');
    http.expectOne('/api/seasons/1950/races/1').flush({ ...archive, race });
    await settle(harness, 'Джерело містить лише дату гонки');
    expect(harness.routeNativeElement!.querySelector('.back-link')?.getAttribute('href')).toBe(
      '/calendar?season=1950',
    );
  });

  it('resets filters when switching seasons and supports browser navigation', async () => {
    const harness = await RouterTestingHarness.create('/calendar');
    http.expectOne('/api/seasons/current/calendar').flush(calendar);
    await settle(harness, 'Abu Dhabi Grand Prix');
    const page = harness.routeNativeElement!;
    page.querySelectorAll<HTMLButtonElement>('.calendar-filters button')[1].click();
    harness.detectChanges();
    const select = page.querySelector<HTMLSelectElement>('select')!;
    select.value = '2025';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/calendar').flush({
        ...calendar,
        season: 2025,
        races: [{ ...pastRace, season: 2025 }],
      }),
    );
    await settle(harness, 'Сезон 2025');
    expect(page.querySelector('.calendar-filters button')?.getAttribute('aria-pressed')).toBe(
      'true',
    );
    expect(page.querySelectorAll('.race-card')).toHaveLength(1);
    await harness.navigateByUrl('/calendar');
    http.expectOne('/api/seasons/current/calendar').flush(calendar);
    await settle(harness, 'Сезон 2026');
    expect(select.value).toBe('2026');
  });

  it('keeps the season selector usable after an API error', async () => {
    const harness = await RouterTestingHarness.create('/calendar?season=2025');
    http
      .expectOne('/api/seasons/2025/calendar')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle(harness, 'Не вдалося завантажити календар');
    const select = harness.routeNativeElement!.querySelector<HTMLSelectElement>('select')!;
    select.value = '2026';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() => http.expectOne('/api/seasons/current/calendar').flush(calendar));
    await settle(harness, 'Сезон 2026');
  });

  it.each(['1949', 'invalid', '2027'])(
    'rejects unsupported season %s without an API request',
    async (season) => {
      const harness = await RouterTestingHarness.create(`/calendar?season=${season}`);
      await settle(harness, 'Сезон не знайдено');
      http.expectNone((request) => request.url.startsWith('/api/'));
    },
  );

  it('renders 404 for an unknown race', async () => {
    const harness = await RouterTestingHarness.create('/calendar/2026/99');
    http
      .expectOne('/api/seasons/2026/races/99')
      .flush({ title: 'Гран-прі не знайдено.' }, { status: 404, statusText: 'Not Found' });
    await settle(harness, 'Гран-прі не знайдено');
    expect(harness.routeNativeElement?.querySelector('.sessions-panel')).toBeNull();
  });

  it('rejects malformed route parameters without sending an API request', async () => {
    const harness = await RouterTestingHarness.create('/calendar/invalid/0');
    await settle(harness, 'Гран-прі не знайдено');
    http.expectNone((request) => request.url.startsWith('/api/'));
  });

  it('recovers from an API error with a retry and handles an empty calendar', async () => {
    const harness = await RouterTestingHarness.create('/calendar');
    http
      .expectOne('/api/seasons/current/calendar')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle(harness, 'Не вдалося завантажити календар');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    harness.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/current/calendar').flush({ ...calendar, races: [] }),
    );
    await settle(harness, 'Календар ще не опублікований');
  });

  it('announces a stale snapshot while keeping the schedule available', async () => {
    const harness = await RouterTestingHarness.create('/calendar');
    http.expectOne('/api/seasons/current/calendar').flush({ ...calendar, isStale: true });
    await settle(harness, 'останній збережений розклад');
    expect(harness.routeNativeElement?.querySelectorAll('.race-card')).toHaveLength(2);
  });
});

describe('Schedule dates', () => {
  it('keeps a date-only session on its announced calendar day', () => {
    const session = { date: '2026-01-01', startsAt: null };
    expect(formatScheduleDate(session)).toBe('1 січня');
    expect(formatScheduleTime(session)).toBe('Час не вказано');
    expect(isPastSchedule(session, new Date(2026, 0, 1, 23).getTime())).toBe(false);
    expect(isPastSchedule(session, new Date(2026, 0, 2, 1).getTime())).toBe(true);
  });
});

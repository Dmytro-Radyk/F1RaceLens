import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { nextHomeRace } from './home-overview';
import type { CalendarResponse, RaceSchedule } from '../calendar/models/calendar.types';
import type { StandingsResponse } from '../standings/standings.types';

const race: RaceSchedule = {
  season: 2026,
  round: 18,
  name: 'United States Grand Prix',
  date: '2026-10-25',
  startsAt: '2026-10-25T20:00:00Z',
  circuit: { id: 'americas', name: 'Circuit of the Americas', locality: 'Austin', country: 'USA' },
  sessions: [{ kind: 'Qualifying', name: 'Кваліфікація', date: '2026-10-24', startsAt: null }],
};
const calendar: CalendarResponse = {
  season: 2026,
  updatedAt: '2026-10-19T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  races: [
    {
      ...race,
      round: 19,
      name: 'Mexico Grand Prix',
      date: '2026-11-01',
      startsAt: '2026-11-01T20:00:00Z',
    },
    race,
    {
      ...race,
      round: 17,
      name: 'Singapore Grand Prix',
      date: '2026-10-11',
      startsAt: '2026-10-11T12:00:00Z',
    },
  ],
};
const drivers: StandingsResponse = {
  season: 2026,
  round: 17,
  updatedAt: calendar.updatedAt,
  isStale: false,
  source: 'Jolpica',
  kind: 'drivers',
  entries: [
    {
      id: 'driver',
      name: 'Test Driver',
      position: 1,
      positionText: '1',
      points: 12.5,
      wins: 1,
      code: null,
      teams: ['Test Team'],
    },
  ],
};
const teams: StandingsResponse = {
  ...drivers,
  kind: 'constructors',
  entries: [{ ...drivers.entries[0], id: 'team', name: 'Test Team' }],
};

describe('Home overview', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.UTC(2026, 9, 19, 12));
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });
  async function respond(
    harness: RouterTestingHarness,
    calendarResult: CalendarResponse | null = calendar,
    driverResult = drivers,
    teamResult = teams,
  ) {
    await vi.waitFor(() => {
      const request = http.expectOne('/api/seasons/current/calendar');
      if (calendarResult) request.flush(calendarResult);
      else request.flush({}, { status: 503, statusText: 'Unavailable' });
      http.expectOne('/api/seasons/current/standings/drivers').flush(driverResult);
      http.expectOne('/api/seasons/current/standings/constructors').flush(teamResult);
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Сезон у фокусі');
    });
  }

  it('opens the root as home with real data links, local schedule and fractional official points', async () => {
    const harness = await RouterTestingHarness.create('/');
    await respond(harness);
    await vi.waitFor(() =>
      expect(harness.routeNativeElement?.querySelector('.leaders-list')?.textContent).toContain(
        '12,5',
      ),
    );
    const page = harness.routeNativeElement!;
    expect(page.querySelector('#next-race-title')?.textContent).toBe(race.name);
    expect(page.textContent).toContain('Час не вказано');
    expect(page.querySelector('a[href="/calendar/2026/18"]')).not.toBeNull();
    expect(page.querySelector('a[href="/drivers/driver?season=2026"]')).not.toBeNull();
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(Array.from(page.querySelectorAll('a'), (link) => link.getAttribute('href'))).toContain(
        '/standings?season=2026&table=constructors',
      );
    });
    expect(page.querySelector('a[href="/predictions"]')).not.toBeNull();
  });

  it('keeps standings visible when the calendar fails and supports retry', async () => {
    const harness = await RouterTestingHarness.create('/home');
    await respond(harness, null);
    await vi.waitFor(() =>
      expect(harness.routeNativeElement?.textContent).toContain('Test Driver'),
    );
    expect(harness.routeNativeElement?.textContent).toContain('Не вдалося завантажити календар');
    const button =
      harness.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!;
    button.click();
    harness.detectChanges();
    await vi.waitFor(() => http.expectOne('/api/seasons/current/calendar').flush(calendar));
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain(race.name);
    });
  });

  it('explains empty pre-season data without fabricated leaders', async () => {
    const harness = await RouterTestingHarness.create('/home');
    await respond(
      harness,
      { ...calendar, races: [] },
      { ...drivers, round: null, entries: [] },
      { ...teams, round: null, entries: [] },
    );
    await vi.waitFor(() =>
      expect(harness.routeNativeElement?.textContent).toContain('Залік сезону ще не опубліковано'),
    );
    expect(harness.routeNativeElement?.textContent).toContain('Календар ще не опубліковано');
    expect(harness.routeNativeElement?.querySelector('.leaders-list')).toBeNull();
  });

  it('keeps the calendar and team table when driver standings fail', async () => {
    const harness = await RouterTestingHarness.create('/home?season=1953');
    await vi.waitFor(() => http.expectOne('/api/seasons/current/calendar').flush(calendar));
    http
      .expectOne('/api/seasons/current/standings/drivers')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/seasons/current/standings/constructors').flush(teams);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Test Team');
      expect(harness.routeNativeElement?.textContent).toContain('Залік тимчасово недоступний');
    });
    expect(harness.routeNativeElement?.querySelector('#next-race-title')?.textContent).toBe(
      race.name,
    );
    expect(harness.routeNativeElement?.querySelector('.season-progress')?.textContent).toContain(
      '—',
    );
  });

  it('shows cached source warnings separately', async () => {
    const harness = await RouterTestingHarness.create('/home');
    await respond(harness, { ...calendar, isStale: true }, { ...drivers, isStale: true });
    await vi.waitFor(() =>
      expect(harness.routeNativeElement?.querySelectorAll('.stale-notice')).toHaveLength(2),
    );
  });

  it('keeps the started race until its result is confirmed', () => {
    const duringRace = Date.UTC(2026, 9, 25, 21);
    expect(nextHomeRace(calendar, drivers, duringRace)?.round).toBe(18);
    expect(nextHomeRace(calendar, undefined, duringRace)?.round).toBe(19);
    expect(nextHomeRace(calendar, { ...drivers, season: 2025, round: 24 }, duringRace)?.round).toBe(
      19,
    );
  });

  it('does not invent another race after the final confirmed round', async () => {
    const harness = await RouterTestingHarness.create('/home');
    await respond(harness, calendar, { ...drivers, round: 19 });
    await vi.waitFor(() =>
      expect(harness.routeNativeElement?.textContent).toContain(
        'Майбутніх етапів у календарі немає',
      ),
    );
    expect(harness.routeNativeElement?.querySelector('#next-race-title')).toBeNull();
  });
});

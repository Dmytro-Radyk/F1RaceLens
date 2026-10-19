import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { analyseRaces } from './analytics';
import type { DriverRace, DriverProfileResponse } from '../drivers/models/driver-profile.types';

function race(
  round: number,
  position = 2,
  grid: number | null = 4,
  status = 'Finished',
): DriverRace {
  return {
    round,
    name: `Grand Prix ${round}`,
    date: '2025-03-01',
    circuit: 'Circuit',
    result: {
      position,
      positionText: String(position),
      grid,
      status,
      driverId: 'test',
      driverName: 'Test Driver',
      driverCode: null,
      team: 'Team',
      laps: 50,
      time: null,
      points: 10,
      q1: null,
      q2: null,
      q3: null,
    },
  };
}
const profile: DriverProfileResponse = {
  season: 2025,
  updatedAt: '2026-10-18T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  driver: { id: 'test', name: 'Test Driver', code: null, nationality: null, dateOfBirth: null },
  standing: {
    id: 'test',
    name: 'Test Driver',
    code: null,
    teams: ['Team'],
    position: 1,
    positionText: '1',
    points: 100,
    wins: 3,
  },
  standingsRound: 24,
  races: Array.from({ length: 10 }, (_, index) => race(index + 1, index < 5 ? 5 : 2)),
};
const list = { ...profile, round: 24, kind: 'drivers', entries: [profile.standing] };

describe('Analytics calculations', () => {
  it('uses distinct recorded participation rounds and compares complete recent windows', () => {
    const result = analyseRaces(profile.races, 5);
    expect(result.recent.events.map((row) => row.round)).toEqual([10, 9, 8, 7, 6]);
    expect(result.previous.events.map((row) => row.round)).toEqual([5, 4, 3, 2, 1]);
    expect(result.improvement).toBe(3);
    expect(result.recent.averageGain).toBe(2);
  });
  it('never treats retirements, DNS or shared cars as a numerical finish', () => {
    const result = analyseRaces(
      [race(1), race(2, 20, 4, 'Engine'), race(3, 2), race(3, 4), race(5, 20, 0, 'Did not start')],
      5,
    );
    expect(result.recent.recorded).toBe(4);
    expect(result.recent.eligible).toBe(3);
    expect(result.recent.ambiguous).toBe(1);
    expect(result.recent.finishes).toBe(1);
    expect(result.recent.otherStatuses).toBe(2);
    expect(result.recent.average).toBe(2);
    expect(result.improvement).toBeNull();
  });
  it('excludes unknown or pit-lane grids from gains and preserves losses and lapped finishes', () => {
    const result = analyseRaces([race(1, 2, 0), race(2, 3, null), race(3, 4, 2, '+1 Lap')], 5);
    expect(result.recent.finishes).toBe(3);
    expect(result.recent.gainSample).toBe(1);
    expect(result.recent.averageGain).toBe(-2);
    expect(result.recent.best).toBe(2);
  });
  it('does not infer a trend from too few actual finishes even in complete windows', () => {
    const races = profile.races.map((row) =>
      row.round > 7 ? race(row.round, 20, 3, 'Engine') : row,
    );
    expect(analyseRaces(races, 5).improvement).toBeNull();
  });
  it('preserves null rather than zero for absent averages and best position', () => {
    const result = analyseRaces([], 5);
    expect(result.recent.average).toBeNull();
    expect(result.recent.best).toBeNull();
    expect(result.recent.averageGain).toBeNull();
    expect(result.scale).toBe(1);
  });
});

describe('Analytics route', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  async function settle(h: RouterTestingHarness, text: string) {
    await vi.waitFor(() => {
      h.detectChanges();
      expect(h.routeNativeElement?.textContent).toContain(text);
    });
  }
  async function load(h: RouterTestingHarness) {
    http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
    await vi.waitFor(() => http.expectOne('/api/seasons/2025/drivers/test').flush(profile));
    await settle(h, 'Результати останнього відрізка');
  }
  it('shows calculations, chart, samples and links for a direct selection', async () => {
    const h = await RouterTestingHarness.create('/insights?season=2025&driver=test&window=5');
    await load(h);
    const page = h.routeNativeElement!;
    expect(page.querySelectorAll('.gain-row')).toHaveLength(5);
    expect(page.querySelector('.metric-grid')?.textContent).toContain('+2');
    expect(page.querySelector('.insight-list')?.textContent).toContain('кращий за попередні');
    expect(page.querySelector('a[href="/calendar/2025/10?session=race"]')).not.toBeNull();
    expect(page.querySelector('.primary-nav a.is-active')?.getAttribute('href')).toBe(
      '/insights?season=2025',
    );
  });
  it('switches window through URL without refetching the profile', async () => {
    const h = await RouterTestingHarness.create('/insights?season=2025&driver=test');
    await load(h);
    const select = h.routeNativeElement!.querySelector<HTMLSelectElement>('#analytics-window')!;
    select.value = '10';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() => {
      h.detectChanges();
      expect(h.routeNativeElement!.querySelectorAll('.gain-row')).toHaveLength(10);
    });
    http.expectNone('/api/seasons/2025/drivers/test');
    expect(h.routeNativeElement!.querySelector('.insight-list')?.textContent).toContain(
      'Висновок про зміну',
    );
  });
  it('does not request an unknown driver and handles unavailable season roster', async () => {
    const h = await RouterTestingHarness.create('/insights?season=2025&driver=unknown');
    http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
    await settle(h, 'Виберіть пілота цього сезону');
    http.expectNone((request) => request.url.includes('/drivers/'));
  });
  it('retries a failed profile and marks stale analytics', async () => {
    const h = await RouterTestingHarness.create('/insights?season=2025');
    http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/2025/drivers/test')
        .flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await settle(h, 'Не вдалося завантажити аналітику');
    h.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    h.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/drivers/test').flush({ ...profile, isStale: true }),
    );
    await settle(h, 'Аналітика розрахована зі збереженого профілю');
  });
  it('does not invent metrics when the selected profile has no races', async () => {
    const h = await RouterTestingHarness.create('/insights?season=2025');
    http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/drivers/test').flush({ ...profile, races: [] }),
    );
    await settle(h, 'Немає результатів для аналітики');
    expect(h.routeNativeElement!.querySelector('.metric-grid')).toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { compareRaces, resultLabel } from './comparison';
import type { DriverProfileResponse, DriverRace } from '../drivers/models/driver-profile.types';

const race = (round: number, position: number, status = 'Finished'): DriverRace => ({
  round,
  name: `Grand Prix ${round}`,
  date: '2025-03-01',
  circuit: 'Test',
  result: {
    position,
    positionText: String(position),
    driverId: 'a',
    driverName: 'Driver A',
    driverCode: null,
    team: 'Team',
    grid: 3,
    laps: 50,
    time: null,
    status,
    points: 10,
    q1: null,
    q2: null,
    q3: null,
  },
});
const profile = (id: string, races: readonly DriverRace[]): DriverProfileResponse => ({
  season: 2025,
  updatedAt: '2026-10-18T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  driver: {
    id,
    name: `Driver ${id.toUpperCase()}`,
    code: null,
    dateOfBirth: null,
    nationality: null,
  },
  standingsRound: 24,
  standing: {
    id,
    name: `Driver ${id.toUpperCase()}`,
    code: null,
    teams: ['Team'],
    position: 1,
    positionText: '1',
    points: 100,
    wins: 2,
  },
  races,
});
const a = profile('a', [race(1, 2), race(2, 15, 'Engine')]);
const b = profile('b', [race(1, 3), race(2, 1)]);
const list = {
  season: 2025,
  round: 24,
  updatedAt: a.updatedAt,
  source: 'Jolpica',
  isStale: false,
  kind: 'drivers',
  entries: [a.standing, b.standing],
};

describe('Comparison rules', () => {
  it('compares only unique common actual finishes and never sums championship points', () => {
    const result = compareRaces(a, b);
    expect(result.comparable).toBe(1);
    expect(result.leftWins).toBe(1);
    expect(result.rightWins).toBe(0);
    expect(result.left.average).toBe(2);
    expect(result.right.average).toBe(2);
    expect(result.rows[1].winner).toBeNull();
  });
  it('excludes shared-car duplicates, skipped rounds and retirements from averages and head-to-head', () => {
    const result = compareRaces(
      profile('a', [race(1, 1), race(1, 4), race(3, 5, '+1 Lap')]),
      profile('b', [race(1, 2), race(2, 3)]),
    );
    expect(result.comparable).toBe(0);
    expect(result.left.finishes).toBe(1);
    expect(result.left.average).toBe(5);
    expect(result.rows).toHaveLength(3);
    expect(result.rows[0].left).toHaveLength(2);
    expect(resultLabel(result.rows[1].left)).toBe('Немає запису');
  });
  it('preserves missing averages and ties', () => {
    expect(compareRaces(profile('a', []), profile('b', [])).left.average).toBeNull();
    expect(compareRaces(a, a).ties).toBe(1);
  });
});

describe('Comparison route', () => {
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
    await vi.waitFor(() => {
      http.expectOne('/api/seasons/2025/drivers/a').flush(a);
      http.expectOne('/api/seasons/2025/drivers/b').flush(b);
    });
    await settle(h, 'Результати за етапами');
  }
  it('loads direct pair, shows independent totals and graph with race links', async () => {
    const h = await RouterTestingHarness.create('/compare?season=2025&first=a&second=b');
    await load(h);
    const page = h.routeNativeElement!;
    expect(page.querySelector('svg[role="img"]')).not.toBeNull();
    expect(page.querySelector('a[href="/calendar/2025/1?session=race"]')).not.toBeNull();
    expect(page.querySelector('tbody')?.textContent).toContain('100');
    expect(page.querySelector('.primary-nav a.is-active')?.getAttribute('href')).toBe(
      '/compare?season=2025',
    );
    page.querySelector<HTMLButtonElement>('.round-buttons button')!.click();
    h.detectChanges();
    expect(page.querySelector('.round-details')?.textContent).toContain('Фініш: 2');
  });
  it.each(['first=a&second=a', 'first=unknown&second=b'])(
    'does not fetch invalid pair %s',
    async (pair) => {
      const h = await RouterTestingHarness.create(`/compare?season=2025&${pair}`);
      http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
      await settle(h, 'Виберіть двох різних');
      http.expectNone((request) => request.url.includes('/drivers/'));
    },
  );
  it('handles one failed profile and retries only that profile', async () => {
    const h = await RouterTestingHarness.create('/compare?season=2025');
    http.expectOne('/api/seasons/2025/standings/drivers').flush(list);
    await vi.waitFor(() => {
      http.expectOne('/api/seasons/2025/drivers/a').flush(a);
      http
        .expectOne('/api/seasons/2025/drivers/b')
        .flush({}, { status: 503, statusText: 'Unavailable' });
    });
    await settle(h, 'Не вдалося завантажити порівняння');
    h.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    h.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/drivers/b').flush({ ...b, isStale: true }),
    );
    await settle(h, 'Профіль другого пілота містить збережені дані');
  });
  it('handles empty season without requesting profiles', async () => {
    const h = await RouterTestingHarness.create('/compare?season=2025');
    http.expectOne('/api/seasons/2025/standings/drivers').flush({ ...list, entries: [] });
    await settle(h, 'Недостатньо пілотів');
  });
});

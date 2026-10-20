import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import type { DriverProfileResponse, DriverRace } from './models/driver-profile.types';
import { buildFormChart } from './utils/form-chart';
import { LocaleService } from '../../core/i18n/locale.service';
import type { Language } from '../../core/i18n/translations';

const race: DriverRace = {
  round: 1,
  name: 'Test Grand Prix',
  date: '2025-03-01',
  circuit: 'Test Circuit',
  result: {
    position: 2,
    positionText: '2',
    driverId: 'test',
    driverName: 'Test Driver',
    driverCode: null,
    team: 'Team A',
    grid: 3,
    laps: 50,
    time: '+1.000',
    status: 'Finished',
    points: 18,
    q1: null,
    q2: null,
    q3: null,
  },
};
const standing = {
  position: 1,
  positionText: '1',
  id: 'test',
  name: 'Test Driver',
  code: null,
  teams: ['Team A', 'Team B'],
  points: 100,
  wins: 3,
};
const profile: DriverProfileResponse = {
  season: 2025,
  updatedAt: '2026-10-18T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  driver: { id: 'test', name: 'Test Driver', code: null, dateOfBirth: null, nationality: null },
  standingsRound: 24,
  standing,
  races: [race],
};

describe('Driver profile routes', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.UTC(2026, 9, 18, 12));
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(async () => {
    http.verify();
    await TestBed.inject(LocaleService).setLanguage('uk');
    localStorage.clear();
    vi.restoreAllMocks();
  });
  async function settle(harness: RouterTestingHarness, text: string) {
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain(text);
    });
  }

  it('shows biography, official season totals, accessible graph and race links', async () => {
    const harness = await RouterTestingHarness.create('/drivers/test?season=2025');
    http.expectOne('/api/seasons/2025/drivers/test').flush(profile);
    await settle(harness, 'Результати за етапами');
    const page = harness.routeNativeElement!;
    expect(page.querySelector('.driver-stats')?.textContent).toContain('100');
    expect(page.querySelector('tbody')?.textContent).toContain('18');
    expect(page.textContent).toContain('Team A · Team B');
    expect(page.textContent).toContain('Не вказано');
    expect(page.querySelector('svg[role="img"]')).not.toBeNull();
    expect(page.querySelector('tbody a')?.getAttribute('href')).toBe(
      '/calendar/2025/1?session=race',
    );
    expect(page.querySelector('.primary-nav a[href="/standings?season=2025"]')).not.toBeNull();
    expect(page.querySelector('.primary-nav .is-active')?.getAttribute('href')).toBe(
      '/drivers?season=2025',
    );
  });

  it('lists season drivers with profile links', async () => {
    const harness = await RouterTestingHarness.create('/drivers?season=2025');
    http
      .expectOne('/api/seasons/2025/standings/drivers')
      .flush({ ...profile, round: 24, kind: 'drivers', entries: [standing] });
    await settle(harness, 'Test Driver');
    expect(harness.routeNativeElement!.querySelector('.driver-card')?.getAttribute('href')).toBe(
      '/drivers/test?season=2025',
    );
  });

  it('updates table statuses and open chart tooltip in all six languages without refetching', async () => {
    const harness = await RouterTestingHarness.create('/drivers/test?season=2025');
    http.expectOne('/api/seasons/2025/drivers/test').flush({
      ...profile,
      driver: { ...profile.driver, nationality: 'Italian' },
      races: [{ ...race, result: { ...race.result, status: 'Retired' } }],
    });
    await settle(harness, 'Результати за етапами');
    const page = harness.routeNativeElement!;
    page.querySelector<HTMLButtonElement>('app-form-chart button')!.click();
    harness.detectChanges();
    const locale = TestBed.inject(LocaleService);
    const expected: readonly [Language, string, string][] = [
      ['uk', 'Схід', 'Старт: 3'],
      ['en', 'Retired', 'Grid: 3'],
      ['de', 'Ausgeschieden', 'Start: 3'],
      ['it', 'Ritirato', 'Partenza: 3'],
      ['es', 'Abandono', 'Salida: 3'],
      ['fr', 'Abandon', 'Départ : 3'],
    ];
    for (const [language, status, grid] of expected) {
      await locale.setLanguage(language);
      harness.detectChanges();
      expect(page.querySelector('tbody')!.textContent).toContain(status);
      expect(page.querySelector('[role="tooltip"]')!.textContent).toContain(status);
      expect(page.querySelector('[role="tooltip"]')!.textContent).toContain(grid);
      expect(page.querySelector('app-form-chart button')!.getAttribute('aria-label')).toContain(
        status,
      );
      http.expectNone('/api/seasons/2025/drivers/test');
    }
  });

  it('switches season without changing identity and handles no participation', async () => {
    const harness = await RouterTestingHarness.create('/drivers/test?season=2025');
    http.expectOne('/api/seasons/2025/drivers/test').flush(profile);
    await settle(harness, 'Результати за етапами');
    const select =
      harness.routeNativeElement!.querySelector<HTMLSelectElement>('.season-picker select')!;
    select.value = '1950';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/1950/drivers/test')
        .flush({ ...profile, season: 1950, standing: null, standingsRound: null, races: [] }),
    );
    await settle(harness, 'Результатів гонок за цей сезон немає');
    expect(harness.routeNativeElement!.querySelector('app-form-chart')).toBeNull();
    expect(harness.routeNativeElement!.querySelector('.driver-stats')?.textContent).toContain('—');
  });

  it('renders a missing driver as 404', async () => {
    const harness = await RouterTestingHarness.create('/drivers/unknown?season=2025');
    http
      .expectOne('/api/seasons/2025/drivers/unknown')
      .flush({}, { status: 404, statusText: 'Not Found' });
    await settle(harness, 'Пілота не знайдено');
  });

  it.each(['/drivers/BAD?season=2025', '/drivers/test?season=1949'])(
    'rejects invalid parameters in %s without HTTP',
    async (url) => {
      const harness = await RouterTestingHarness.create(url);
      await settle(harness, 'не знайдено');
      http.expectNone((request) => request.url.startsWith('/api/'));
    },
  );

  it('retries an unavailable profile and marks stale data', async () => {
    const harness = await RouterTestingHarness.create('/drivers/test?season=2025');
    http
      .expectOne('/api/seasons/2025/drivers/test')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle(harness, 'Не вдалося завантажити профіль');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    harness.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/drivers/test').flush({ ...profile, isStale: true }),
    );
    await settle(harness, 'Профіль або його залік містить збережені дані');
  });
});

describe('Driver form chart', () => {
  it('breaks finish lines for retirements, unknown grids and skipped rounds', () => {
    const chart = buildFormChart([
      race,
      { ...race, round: 2, result: { ...race.result, status: 'Engine', grid: 0 } },
      { ...race, round: 4 },
    ]);
    expect(chart.finish.match(/M/g)).toHaveLength(2);
    expect(chart.finish).not.toContain('L');
    expect(chart.points[1].finished).toBe(false);
    expect(chart.points[1].y).toBe(290);
    expect(chart.points[1].gridY).toBeNull();
  });
  it('accepts a lapped finish and shows lower positions higher on the axis', () => {
    const chart = buildFormChart([
      race,
      { ...race, round: 2, result: { ...race.result, status: '+1 Lap', position: 5 } },
    ]);
    expect(chart.finish).toContain('L');
    expect(chart.points[0].y).toBeLessThan(chart.points[1].y);
    expect(chart.points[1].finished).toBe(true);
  });
  it('never chooses a fabricated best result for multiple cars in one historic round', () => {
    const chart = buildFormChart([race, { ...race, result: { ...race.result, position: 4 } }]);
    expect(chart.finish).toBe('');
    expect(chart.start).toBe('');
    expect(chart.points).toHaveLength(1);
    expect(chart.points[0].label).toContain('кілька записів');
  });
});

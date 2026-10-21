import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import type { StandingsResponse } from '../standings/standings.types';

const teams: StandingsResponse = {
  season: 2025,
  round: 24,
  kind: 'constructors',
  updatedAt: '2026-10-18T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  entries: [
    {
      id: 'team_a',
      name: 'Team A',
      position: null,
      positionText: 'E',
      code: null,
      teams: [],
      points: -0.5,
      wins: 2,
    },
  ],
};
const drivers: StandingsResponse = {
  ...teams,
  kind: 'drivers',
  entries: [
    {
      ...teams.entries[0],
      id: 'transfer',
      name: 'Transfer Driver',
      teams: ['Team B', 'Team A'],
      points: 100,
    },
    { ...teams.entries[0], id: 'other', name: 'Other Driver', teams: ['Team B'] },
  ],
};

describe('Team routes', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  async function settle(harness: RouterTestingHarness, text: string) {
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain(text);
    });
  }

  it('shows excluded teams, fractional negative points and season links without loading rosters', async () => {
    const harness = await RouterTestingHarness.create('/teams?season=2025');
    http.expectOne('/api/seasons/2025/standings/constructors').flush(teams);
    await settle(harness, 'Team A');
    const page = harness.routeNativeElement!;
    expect(page.textContent).toContain('Виключено');
    expect(page.textContent).toContain('-0,5');
    expect(page.querySelector('.team-card')?.getAttribute('href')).toBe(
      '/teams/team_a?season=2025',
    );
    expect(page.querySelector('.primary-nav a.is-active')?.getAttribute('href')).toBe(
      '/teams',
    );
    http.expectNone('/api/seasons/2025/standings/drivers');
  });

  it('includes transferred drivers without counting their points as team totals', async () => {
    const harness = await RouterTestingHarness.create('/teams/team_a?season=2025');
    http.expectOne('/api/seasons/2025/standings/constructors').flush(teams);
    await vi.waitFor(() => http.expectOne('/api/seasons/2025/standings/drivers').flush(drivers));
    await settle(harness, 'Transfer Driver');
    const page = harness.routeNativeElement!;
    expect(page.querySelector('.team-hero')?.textContent).toContain('-0,5');
    expect(page.querySelector('.team-roster')?.textContent).not.toContain('Other Driver');
    expect(page.querySelector('.team-roster')?.textContent).toContain('100 очок за сезон');
    expect(page.querySelector('.team-roster a')?.getAttribute('href')).toBe(
      '/drivers/transfer?season=2025',
    );
  });

  it('keeps team information when roster fails and supports independent retry', async () => {
    const harness = await RouterTestingHarness.create('/teams/team_a?season=2025');
    http.expectOne('/api/seasons/2025/standings/constructors').flush(teams);
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/2025/standings/drivers')
        .flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await settle(harness, 'Не вдалося завантажити пілотів');
    expect(harness.routeNativeElement!.querySelector('.team-hero')?.textContent).toContain(
      'Team A',
    );
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.team-roster button')!.click();
    harness.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2025/standings/drivers').flush({ ...drivers, isStale: true }),
    );
    await settle(harness, 'Показуємо збережений залік пілотів');
  });

  it('changes season on the same team and never retains its previous roster', async () => {
    const harness = await RouterTestingHarness.create('/teams/team_a?season=2025');
    http.expectOne('/api/seasons/2025/standings/constructors').flush(teams);
    await vi.waitFor(() => http.expectOne('/api/seasons/2025/standings/drivers').flush(drivers));
    await settle(harness, 'Transfer Driver');
    const select =
      harness.routeNativeElement!.querySelector<HTMLSelectElement>('.season-picker select')!;
    select.value = '1950';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/1950/participants/teams')
        .flush({ ...teams, season: 1950, round: null, entries: [] }),
    );
    await settle(harness, 'Конструктора немає серед учасників');
    expect(harness.routeNativeElement!.textContent).not.toContain('Transfer Driver');
    http.expectNone('/api/seasons/1950/standings/drivers');
  });

  it('explains an absent historical constructors championship', async () => {
    const harness = await RouterTestingHarness.create('/teams?season=1950');
    http
      .expectOne('/api/seasons/1950/participants/teams')
      .flush({ ...teams, season: 1950, entries: [{ id: 'ferrari', name: 'Ferrari' }] });
    await settle(harness, 'Кубок конструкторів ще не проводився');
    await settle(harness, 'Ferrari');
    expect(harness.routeNativeElement!.querySelector('.team-card')?.getAttribute('href')).toBe(
      '/teams/ferrari?season=1950',
    );
    expect(harness.routeNativeElement!.querySelector('.team-stats')).toBeNull();
  });

  it('loads historical drivers by constructor identity without inventing championship statistics', async () => {
    const harness = await RouterTestingHarness.create('/teams/ferrari?season=1953');
    http
      .expectOne('/api/seasons/1953/participants/teams')
      .flush({ ...teams, season: 1953, entries: [{ id: 'ferrari', name: 'Ferrari' }] });
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/1953/participants/teams/ferrari/drivers')
        .flush({ ...teams, season: 1953, entries: [{ id: 'ascari', name: 'Alberto Ascari' }] }),
    );
    await settle(harness, 'Alberto Ascari');
    expect(harness.routeNativeElement!.querySelector('.team-card')?.getAttribute('href')).toBe(
      '/drivers/ascari?season=1953',
    );
    expect(harness.routeNativeElement!.querySelector('.team-stats')).toBeNull();
    http.expectNone((request) => request.url.includes('/standings/'));
  });

  it.each(['/teams?season=1949', '/teams/BAD?season=2025'])(
    'rejects invalid route %s without requests',
    async (url) => {
      const harness = await RouterTestingHarness.create(url);
      await settle(harness, 'Сторінку не знайдено');
      http.expectNone((request) => request.url.startsWith('/api/'));
    },
  );
});

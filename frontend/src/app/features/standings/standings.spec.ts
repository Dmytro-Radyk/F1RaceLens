import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import type { StandingsEntry, StandingsResponse } from './standings.types';

const row: StandingsEntry = {
  position: 1,
  positionText: '1',
  id: 'test',
  name: 'Test Driver',
  code: null,
  teams: ['Team A', 'Team B'],
  points: 4.5,
  wins: 0,
};
const data: StandingsResponse = {
  season: 2025,
  round: 24,
  kind: 'drivers',
  updatedAt: '2026-10-17T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
  entries: [row],
};

describe('Season standings', () => {
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
  async function respond(harness: RouterTestingHarness, url: string, response = data) {
    await vi.waitFor(() => http.expectOne(url).flush(response));
    await settle(
      harness,
      response.entries.length ? response.entries[0].name : 'Залік поки недоступний',
    );
  }

  it('loads a historical driver table with every team, fractional points and exclusions', async () => {
    const harness = await RouterTestingHarness.create('/standings?season=2025');
    await respond(harness, '/api/seasons/2025/standings/drivers', {
      ...data,
      entries: [
        row,
        {
          ...row,
          id: 'excluded',
          name: 'Excluded Driver',
          position: null,
          positionText: 'D',
          points: 0,
        },
      ],
    });
    const page = harness.routeNativeElement!;
    expect(page.querySelector('tbody')?.textContent).toContain('Team A · Team B');
    expect(page.querySelector('tbody')?.textContent).toContain('4,5');
    expect(page.querySelector('tbody')?.textContent).toContain('Дискваліфіковано');
    expect(page.querySelector('tbody tr:last-child')?.classList.contains('podium')).toBe(false);
    expect(page.querySelector<HTMLSelectElement>('select')?.value).toBe('2025');
    expect(page.textContent).toContain('Підсумковий залік сезону');
    expect(page.textContent).toContain('Після етапу 24');
  });

  it('requests the provider current season and displays the actual round', async () => {
    const harness = await RouterTestingHarness.create('/standings');
    await respond(harness, '/api/seasons/current/standings/drivers', {
      ...data,
      season: 2026,
      round: 17,
    });
    expect(harness.routeNativeElement!.textContent).toContain('Останній доступний залік');
    expect(harness.routeNativeElement!.textContent).toContain('Після етапу 17');
  });

  it('switches tables while preserving the year and supports returning to drivers', async () => {
    const harness = await RouterTestingHarness.create('/standings?season=2025');
    await respond(harness, '/api/seasons/2025/standings/drivers');
    const link = harness.routeNativeElement!.querySelector<HTMLAnchorElement>(
      '.session-navigation a:last-child',
    )!;
    expect(link.getAttribute('href')).toBe('/standings?season=2025&table=constructors');
    await harness.navigateByUrl(link.getAttribute('href')!);
    await respond(harness, '/api/seasons/2025/standings/constructors', {
      ...data,
      kind: 'constructors',
      entries: [{ ...row, name: 'Team A' }],
    });
    expect(harness.routeNativeElement!.querySelectorAll('thead th')).toHaveLength(4);
    await harness.navigateByUrl('/standings?season=2025');
    await respond(harness, '/api/seasons/2025/standings/drivers');
    expect(harness.routeNativeElement!.querySelectorAll('thead th')).toHaveLength(5);
  });

  it('switches years in a constructor table and explains absent historical data', async () => {
    const harness = await RouterTestingHarness.create('/standings?season=2025&table=constructors');
    await respond(harness, '/api/seasons/2025/standings/constructors', {
      ...data,
      kind: 'constructors',
    });
    const select = harness.routeNativeElement!.querySelector<HTMLSelectElement>('select')!;
    select.value = '1950';
    select.dispatchEvent(new Event('change'));
    await respond(harness, '/api/seasons/1950/standings/constructors', {
      ...data,
      season: 1950,
      round: null,
      kind: 'constructors',
      entries: [],
    });
    expect(harness.routeNativeElement!.textContent).toContain('Джерело не містить заліку команд');
    expect(harness.routeNativeElement!.querySelector('table')).toBeNull();
  });

  it('preserves season in both sidebar directions and on a Grand Prix route', async () => {
    const harness = await RouterTestingHarness.create('/calendar?season=2025');
    http.expectOne('/api/seasons/2025/calendar').flush({ ...data, races: [] });
    await settle(harness, 'Календар ще не опублікований');
    const standingsLink = harness.routeNativeElement!.querySelector<HTMLAnchorElement>(
      '.primary-nav a[href="/standings?season=2025"]',
    )!;
    expect(standingsLink).not.toBeNull();
    await harness.navigateByUrl(standingsLink.getAttribute('href')!);
    await respond(harness, '/api/seasons/2025/standings/drivers');
    expect(
      harness.routeNativeElement!.querySelector('.primary-nav a[href="/calendar?season=2025"]'),
    ).not.toBeNull();
    await harness.navigateByUrl('/calendar/2025/1');
    http
      .expectOne('/api/seasons/2025/races/1')
      .flush({
        ...data,
        race: {
          season: 2025,
          round: 1,
          name: 'Test Grand Prix',
          circuit: { name: 'Test Circuit', locality: 'Test', country: 'UK', id: 'test' },
          date: '2025-03-01',
          startsAt: null,
          sessions: [],
        },
      });
    await settle(harness, 'Test Grand Prix');
    expect(
      harness.routeNativeElement!.querySelector('.primary-nav a[href="/standings?season=2025"]'),
    ).not.toBeNull();
    expect(
      harness.routeNativeElement!.querySelector('.primary-nav .is-active')?.getAttribute('href'),
    ).toBe('/calendar?season=2025');
  });

  it('retries a source failure and marks stale data', async () => {
    const harness = await RouterTestingHarness.create('/standings?season=2025');
    http
      .expectOne('/api/seasons/2025/standings/drivers')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle(harness, 'Не вдалося завантажити турнірну таблицю');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    harness.detectChanges();
    await respond(harness, '/api/seasons/2025/standings/drivers', { ...data, isStale: true });
    expect(harness.routeNativeElement!.textContent).toContain('останню збережену турнірну таблицю');
  });

  it('cancels the old request when changing table during loading', async () => {
    const harness = await RouterTestingHarness.create('/standings?season=2025');
    const request = http.expectOne('/api/seasons/2025/standings/drivers');
    await harness.navigateByUrl('/standings?season=2025&table=constructors');
    await respond(harness, '/api/seasons/2025/standings/constructors', {
      ...data,
      kind: 'constructors',
    });
    expect(request.cancelled).toBe(true);
  });

  it.each(['1949', 'bad', '2027'])(
    'rejects unsupported year %s without HTTP calls',
    async (season) => {
      const harness = await RouterTestingHarness.create(`/standings?season=${season}`);
      await settle(harness, 'Сезон не знайдено');
      http.expectNone((request) => request.url.startsWith('/api/'));
    },
  );
});

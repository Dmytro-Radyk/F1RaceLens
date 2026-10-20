import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import type { RaceSchedule } from './models/calendar.types';
import type { DriverResult, ResultKind } from './models/results.types';

const race: RaceSchedule = {
  season: 2025,
  round: 2,
  name: 'Chinese Grand Prix',
  circuit: {
    id: 'shanghai',
    name: 'Shanghai International Circuit',
    locality: 'Shanghai',
    country: 'China',
  },
  date: '2025-03-23',
  startsAt: '2025-03-23T07:00:00Z',
  sessions: [
    { kind: 'Race', name: 'Гонка', date: '2025-03-23', startsAt: null },
    { kind: 'Sprint', name: 'Спринт', date: '2025-03-22', startsAt: null },
  ],
};
const row: DriverResult = {
  position: 12,
  positionText: 'R',
  driverId: 'test',
  driverName: 'Test Driver',
  driverCode: null,
  team: null,
  grid: 0,
  laps: 12,
  time: null,
  status: 'Engine',
  points: 4.5,
  q1: '1:20.000',
  q2: null,
  q3: null,
};
const metadata = {
  season: 2025,
  updatedAt: '2026-10-17T12:00:00Z',
  isStale: false,
  source: 'Jolpica',
};

describe('Grand Prix results', () => {
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
  async function open(kind: string, selectedRace = race) {
    const harness = await RouterTestingHarness.create(
      `/calendar/${selectedRace.season}/${selectedRace.round}?session=${kind}`,
    );
    http
      .expectOne(`/api/seasons/${selectedRace.season}/races/${selectedRace.round}`)
      .flush({ ...metadata, season: selectedRace.season, race: selectedRace });
    await settle(harness, selectedRace.name);
    return harness;
  }
  async function respond(
    harness: RouterTestingHarness,
    kind: ResultKind,
    entries: readonly DriverResult[] = [row],
    selectedRace = race,
    isStale = false,
  ) {
    await vi.waitFor(() =>
      http
        .expectOne(
          `/api/seasons/${selectedRace.season}/races/${selectedRace.round}/results/${kind}`,
        )
        .flush({
          ...metadata,
          season: selectedRace.season,
          round: selectedRace.round,
          kind,
          entries,
          isStale,
        }),
    );
    await settle(harness, entries.length ? 'Test Driver' : 'результат');
  }

  it('shows classifications, retirements, fractional points and unknown fields without fabrication', async () => {
    const harness = await open('race');
    await respond(harness, 'race');
    const table = harness.routeNativeElement!.querySelector('table')!;
    expect(table.textContent).toContain('Схід: двигун');
    expect(table.textContent).toContain('4,5');
    expect(table.textContent).toContain('0*');
    expect(table.textContent).toContain('—');
    expect(
      harness
        .routeNativeElement!.querySelector('.session-navigation [aria-current="page"]')
        ?.textContent?.trim(),
    ).toBe('Гонка');
  });

  it('changes result requests when navigating between qualifying and sprint links', async () => {
    const harness = await open('qualifying');
    await respond(harness, 'qualifying');
    expect(harness.routeNativeElement!.querySelector('table')?.textContent).toContain('1:20.000');
    expect(harness.routeNativeElement!.querySelectorAll('thead th')).toHaveLength(6);
    await harness.navigateByUrl('/calendar/2025/2?session=sprint');
    await respond(harness, 'sprint', [{ ...row, status: 'Finished' }]);
    expect(harness.routeNativeElement!.querySelector('table')?.textContent).toContain('Фінішував');
    expect(harness.routeNativeElement!.querySelectorAll('thead th')).toHaveLength(8);
  });

  it('does not request results for the schedule or an unscheduled sprint', async () => {
    const harness = await open('sprint', { ...race, sessions: [race.sessions[0]] });
    await settle(harness, 'Розклад вікенду');
    expect(harness.routeNativeElement!.querySelector('app-results-table')).toBeNull();
    expect(
      harness.routeNativeElement!.querySelector('.session-navigation')?.textContent,
    ).not.toContain('Спринт');
    http.expectNone((request) => request.url.includes('/results/'));
  });

  it.each([false, true])(
    'distinguishes historical missing data from upcoming results (future=%s)',
    async (future) => {
      const selectedRace = future
        ? { ...race, season: 2026, date: '2026-12-31', startsAt: null }
        : race;
      const harness = await open('race', selectedRace);
      await respond(harness, 'race', [], selectedRace);
      await settle(
        harness,
        future ? 'Результатів ще немає' : 'Джерело не надало результатів цієї сесії',
      );
      expect(harness.routeNativeElement!.querySelector('table')).toBeNull();
    },
  );

  it('retries provider failures and announces a stale snapshot', async () => {
    const harness = await open('race');
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/2025/races/2/results/race')
        .flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await settle(harness, 'Не вдалося завантажити результати');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.results-panel button')!.click();
    harness.detectChanges();
    await respond(harness, 'race', [row], race, true);
    await settle(harness, 'останні збережені результати');
  });
});

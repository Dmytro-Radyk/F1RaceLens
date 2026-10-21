import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import axe from 'axe-core';
import { AuthService } from '../../auth/services/auth.service';
import type { Account } from '../../auth/models/auth.types';
import { PersonalHome } from './components/personal-home';
import type { StandingsResponse } from '../standings/standings.types';
import type { CalendarResponse } from '../calendar/models/calendar.types';

const account: Account = {
  id: 'one',
  login: 'first-user',
  email: 'first@example.test',
  emailConfirmed: false,
  language: 'uk',
  theme: 'dark',
  driverIds: ['driver', 'archived'],
  teamIds: ['team'],
  favourites: [
    { kind: 'driver', id: 'driver', name: 'Test Driver' },
    { kind: 'team', id: 'team', name: 'Test Team' },
    { kind: 'driver', id: 'archived', name: 'Archived Driver' },
  ],
};
const drivers: StandingsResponse = {
  season: 2026,
  round: 17,
  source: 'Jolpica',
  updatedAt: '2026-10-20T00:00:00Z',
  isStale: false,
  kind: 'drivers',
  entries: [
    {
      id: 'driver',
      name: 'Test Driver',
      position: 1,
      positionText: '1',
      code: null,
      teams: ['Test Team'],
      wins: 2,
      points: 12.5,
    },
  ],
};
const teams: StandingsResponse = {
  ...drivers,
  kind: 'constructors',
  entries: [{ ...drivers.entries[0], id: 'team', name: 'Test Team', points: 20 }],
};
const calendar: CalendarResponse = {
  season: 2026,
  source: 'Jolpica',
  updatedAt: drivers.updatedAt,
  isStale: false,
  races: [
    {
      season: 2026,
      round: 17,
      name: 'Singapore Grand Prix',
      date: '2026-10-11',
      startsAt: null,
      circuit: {
        id: 'singapore',
        name: 'Test Circuit',
        locality: 'Singapore',
        country: 'Singapore',
      },
      sessions: [],
    },
  ],
};
const results = {
  season: 2026,
  round: 17,
  source: 'Jolpica',
  updatedAt: drivers.updatedAt,
  isStale: false,
  kind: 'race',
  entries: [
    {
      driverId: 'driver',
      driverName: 'Test Driver',
      team: 'Test Team',
      position: 16,
      positionText: '16',
      status: 'Retired',
      points: 0,
    },
    {
      driverId: 'teammate',
      driverName: 'Teammate',
      team: 'Test Team',
      position: 7,
      positionText: '7',
      status: 'Finished',
      points: 4.5,
    },
  ],
};

describe('Personal home', () => {
  const user = signal<Account | null>(null);
  let http: HttpTestingController;
  beforeEach(() => {
    localStorage.clear();
    user.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const fixture = TestBed.createComponent(PersonalHome);
    fixture.componentRef.setInput('calendar', calendar);
    fixture.componentRef.setInput('drivers', drivers);
    fixture.componentRef.setInput('teams', teams);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }
  it('makes no result requests for guests or accounts without favourites', () => {
    const { fixture, page } = setup();
    expect(page.querySelector('section')).toBeNull();
    user.set({ ...account, favourites: [], driverIds: [], teamIds: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('Оберіть пілотів і команди');
    http.expectNone('/api/seasons/2026/races/17/results/race');
  });
  it('shows saved favourites with current standings, honest retirement status and race-only team points', async () => {
    user.set(account);
    const { fixture, page } = setup();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2026/races/17/results/race').flush(results),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Схід');
    });
    expect(page.textContent).toContain('12,5');
    expect(page.textContent).toContain('4,5');
    expect(page.querySelectorAll('.favourite-card')).toHaveLength(3);
    expect(page.textContent).toContain('У поточному заліку немає запису.');
    expect(page.querySelector('a[href="/drivers/driver?season=2026"]')).not.toBeNull();
    expect(page.querySelector('a[href="/search?q=Archived%20Driver"]')).not.toBeNull();
    expect(
      (
        await axe.run(page, {
          rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
        })
      ).violations,
    ).toEqual([]);
  });
  it('does not mix standings from different years or fetch an unconfirmed race', () => {
    user.set(account);
    const fixture = TestBed.createComponent(PersonalHome);
    fixture.componentRef.setInput('calendar', calendar);
    fixture.componentRef.setInput('drivers', {
      ...drivers,
      season: 2025,
      entries: [{ ...drivers.entries[0], points: 9999 }],
    });
    fixture.componentRef.setInput('teams', { ...teams, season: 2025 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('9999');
    http.expectNone('/api/seasons/2026/races/17/results/race');
  });
  it('keeps standings during results failure, retries and clears the panel on account changes', async () => {
    user.set(account);
    const { fixture, page } = setup();
    await vi.waitFor(() =>
      http
        .expectOne('/api/seasons/2026/races/17/results/race')
        .flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Не вдалося завантажити результати');
    });
    expect(page.textContent).toContain('12,5');
    page.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/2026/races/17/results/race').flush(results),
    );
    user.set({
      ...account,
      id: 'two',
      login: 'second-user',
      favourites: [],
      driverIds: [],
      teamIds: [],
    });
    fixture.detectChanges();
    expect(page.textContent).not.toContain('Test Driver');
    expect(page.textContent).not.toContain('first-user');
  });
});

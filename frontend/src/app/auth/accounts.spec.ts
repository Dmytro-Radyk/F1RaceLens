import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { AuthService } from './services/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/services/theme.service';
import { routes } from '../app.routes';
import type { Account } from './models/auth.types';
import axe from 'axe-core';
import { CATALOG_LOADER, loadCatalog } from '../core/i18n/translations';

const account: Account = {
  id: 'account-one',
  login: 'first-user',
  email: 'first@example.test',
  language: 'uk',
  theme: 'dark',
  driverIds: [],
  teamIds: [],
  favourites: [],
  emailConfirmed: false,
};
describe('Persistent accounts', () => {
  let http: HttpTestingController;
  let catalogLoader = vi.fn(loadCatalog);
  beforeEach(() => {
    localStorage.clear();
    catalogLoader = vi.fn(loadCatalog);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: CATALOG_LOADER, useValue: catalogLoader },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(async () => {
    http.verify();
    await TestBed.inject(LocaleService).setLanguage('uk');
    localStorage.clear();
  });
  async function restore(value: Account = account) {
    const auth = TestBed.inject(AuthService);
    const pending = auth.restore();
    http.expectOne('/api/auth/me').flush(value);
    await pending;
    TestBed.tick();
    return auth;
  }
  async function csrf() {
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/auth/csrf').flush(null);
    });
  }
  it('restores the server session and preferences without persisting credentials in browser storage', async () => {
    const auth = await restore({ ...account, language: 'fr', theme: 'light' });
    expect(auth.isAuthenticated()).toBe(true);
    expect(TestBed.inject(LocaleService).language()).toBe('fr');
    expect(TestBed.inject(ThemeService).mode()).toBe('light');
    expect(Object.keys(localStorage).sort()).toEqual([
      'f1-race-lens-language',
      'f1-race-lens-theme',
    ]);
    expect(JSON.stringify(localStorage)).not.toMatch(/password|accessToken|refreshToken/);
  });
  it('keeps public pages available when no server session exists and protects the profile', async () => {
    const navigation = RouterTestingHarness.create('/profile');
    await vi.waitFor(() =>
      http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' }),
    );
    const harness = await navigation;
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fprofile');
    expect(harness.routeNativeElement!.textContent).toContain('Увійти');
  });
  it('does not pretend to log out when the server cannot clear the session', async () => {
    const auth = await restore();
    const operation = auth.logout();
    const outcome = expect(operation).rejects.toBeDefined();
    await csrf();
    await vi.waitFor(() =>
      http.expectOne('/api/auth/logout').flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await outcome;
    expect(auth.isAuthenticated()).toBe(true);
  });
  it('saves global preference changes after obtaining an identity-bound CSRF token', async () => {
    const auth = await restore();
    TestBed.inject(ThemeService).set('light');
    await TestBed.inject(LocaleService).setLanguage('de');
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/preferences');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ language: 'de', theme: 'light' });
      request.flush({ ...account, language: 'de', theme: 'light' });
    });
    await vi.waitFor(() => expect(auth.user()?.theme).toBe('light'));
  });
  it('preserves unsaved favourites when automatic preferences are saved and shows archived names', async () => {
    const auth = await restore({
      ...account,
      driverIds: ['old_driver'],
      favourites: [{ kind: 'driver', id: 'old_driver', name: 'Historic Driver' }],
    });
    const harness = await RouterTestingHarness.create('/profile');
    for (const kind of ['drivers', 'constructors']) {
      http
        .expectOne('/api/seasons/current/standings/' + kind)
        .flush({ entries: kind === 'drivers' ? [{ id: 'test_driver', name: 'Test Driver' }] : [] });
    }
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement!.textContent).toContain('Test Driver');
    });
    const page = harness.routeNativeElement!;
    expect(page.textContent).toContain('Historic Driver');
    const checkbox = page.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    harness.detectChanges();
    TestBed.inject(ThemeService).set('light');
    await csrf();
    await vi.waitFor(() =>
      http.expectOne('/api/auth/preferences').flush({ ...auth.user(), theme: 'light' }),
    );
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(auth.user()?.theme).toBe('light');
    });
    expect(checkbox.checked).toBe(true);
    page.querySelector<HTMLButtonElement>('button.primary-link')!.click();
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/favourites');
      expect(request.request.body).toEqual({
        driverIds: ['old_driver', 'test_driver'],
        teamIds: [],
      });
      request.flush({
        ...auth.user(),
        driverIds: ['old_driver', 'test_driver'],
        favourites: [
          ...auth.user()!.favourites,
          { kind: 'driver', id: 'test_driver', name: 'Test Driver' },
        ],
      });
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.textContent).toContain('Обране збережено.');
    });
    expect(page.querySelector('a[href="/drivers/test_driver"]')!.textContent).toBe('Test Driver');
    expect(
      (await axe.run(page, { rules: { 'color-contrast': { enabled: false } } })).violations,
    ).toEqual([]);
  });
  it('expires the client session when a protected mutation returns 401', async () => {
    const auth = await restore();
    const operation = auth.saveFavourites([], []);
    const outcome = expect(operation).rejects.toBeDefined();
    await csrf();
    await vi.waitFor(() =>
      http.expectOne('/api/auth/favourites').flush({}, { status: 401, statusText: 'Unauthorized' }),
    );
    await outcome;
    expect(auth.isAuthenticated()).toBe(false);
  });
  it('does not overwrite the saved language when its catalog cannot be loaded', async () => {
    catalogLoader.mockRejectedValue(new Error('offline catalog'));
    const auth = await restore({ ...account, language: 'fr' });
    expect(TestBed.inject(LocaleService).failed()).toBe(true);
    expect(auth.user()?.language).toBe('fr');
    await new Promise((resolve) => setTimeout(resolve, 450));
    TestBed.tick();
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/preferences');
  });
  it('keeps saved favourites visible when the current sporting lists are unavailable', async () => {
    await restore({
      ...account,
      driverIds: ['old_driver'],
      favourites: [{ kind: 'driver', id: 'old_driver', name: 'Historic Driver' }],
    });
    const harness = await RouterTestingHarness.create('/profile');
    for (const kind of ['drivers', 'constructors']) {
      http
        .expectOne('/api/seasons/current/standings/' + kind)
        .flush({}, { status: 503, statusText: 'Unavailable' });
    }
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement!.textContent).toContain(
        'Збережене обране залишається доступним.',
      );
    });
    expect(harness.routeNativeElement!.textContent).toContain('Historic Driver');
    expect(
      harness.routeNativeElement!.querySelector<HTMLButtonElement>('button.primary-link')!.disabled,
    ).toBe(true);
    http.expectNone('/api/auth/favourites');
  });
});

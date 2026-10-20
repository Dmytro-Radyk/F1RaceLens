import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { App } from './app';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { routes } from './app.routes';
import axe from 'axe-core';
const account = {
  id: 'test-user',
  login: 'test-user',
  email: 'test@example.test',
  language: 'uk',
  theme: 'dark',
  driverIds: [],
  teamIds: [],
  favourites: [],
};

describe('Authentication screen', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    await TestBed.inject(Router).navigateByUrl('/login');
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('starts in dark mode and preserves the selected theme across authentication screens', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.getAttribute('data-theme')).toBe('dark');

    page.querySelector<HTMLButtonElement>('.auth-theme-toggle')!.click();
    await fixture.whenStable();
    expect(page.getAttribute('data-theme')).toBe('light');
    expect(localStorage.getItem('f1-race-lens-theme')).toBe('light');

    page.querySelector<HTMLButtonElement>('.auth-switch button')!.click();
    await fixture.whenStable();
    expect(page.querySelector('app-register-form')).toBeTruthy();
    expect(page.getAttribute('data-theme')).toBe('light');
  });

  it('restores a saved theme when the application is created', async () => {
    localStorage.setItem('f1-race-lens-theme', 'light');
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.nativeElement.getAttribute('data-theme')).toBe('light');
  });

  it('allows theme switching when browser storage is blocked', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage blocked', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Storage blocked', 'SecurityError');
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.getAttribute('data-theme')).toBe('dark');
    page.querySelector<HTMLButtonElement>('.auth-theme-toggle')!.click();
    await fixture.whenStable();
    expect(page.getAttribute('data-theme')).toBe('light');
  });

  it('shows login and registration forms', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toBe('Увійти');
    expect(page.querySelector('.auth-brand-panel')).toBeTruthy();
    expect(page.querySelectorAll('.auth-social button:disabled')).toHaveLength(2);
    expect(page.querySelector('.auth-helper a')).toBeTruthy();
    expect(
      (await axe.run(page, { rules: { 'color-contrast': { enabled: false } } })).violations,
    ).toEqual([]);

    page.querySelector<HTMLButtonElement>('.auth-switch button')!.click();
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('Реєстрація');
    expect(page.querySelector('app-register-form form')).toBeTruthy();
    expect(
      (await axe.run(page, { rules: { 'color-contrast': { enabled: false } } })).violations,
    ).toEqual([]);
  });

  it('rejects empty fields and wrong credentials', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(page.querySelectorAll('.field-error')).toHaveLength(2);

    const login = page.querySelector<HTMLInputElement>('#login')!;
    login.value = 'visitor';
    login.dispatchEvent(new Event('input', { bubbles: true }));
    const password = page.querySelector<HTMLInputElement>('#login-password')!;
    password.value = 'wrong';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/csrf').flush(null),
    );
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController)
        .expectOne('/api/auth/login')
        .flush({}, { status: 401, statusText: 'Unauthorized' }),
    );
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('.form-error')?.textContent).toContain('Невірний');
    });
    expect(page.querySelector('.app-shell')).toBeNull();
  });

  it('validates registration and shows a safe server error without creating a session', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.auth-switch button')!.click();
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(page.querySelectorAll('.field-error')).toHaveLength(3);

    const login = page.querySelector<HTMLInputElement>('#register-login')!;
    login.value = 'new-user';
    login.dispatchEvent(new Event('input', { bubbles: true }));
    const email = page.querySelector<HTMLInputElement>('#register-email')!;
    email.value = 'test@example.test';
    email.dispatchEvent(new Event('input', { bubbles: true }));
    const password = page.querySelector<HTMLInputElement>('#register-password')!;
    password.value = 'test-password';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/csrf').flush(null),
    );
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController)
        .expectOne('/api/auth/register')
        .flush({ code: 'registration-unavailable' }, { status: 400, statusText: 'Bad Request' }),
    );
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('.form-error')?.textContent).toContain('Не вдалося створити');
    });
    expect(page.querySelector('.app-shell')).toBeNull();
  });

  it('opens the application after server login and waits for server logout', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const login = page.querySelector<HTMLInputElement>('#login')!;
    login.value = 'test-user';
    login.dispatchEvent(new Event('input', { bubbles: true }));
    const password = page.querySelector<HTMLInputElement>('#login-password')!;
    password.value = 'test-password';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/csrf').flush(null),
    );
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/login').flush(account),
    );
    await vi.waitFor(() => {
      TestBed.inject(HttpTestingController).expectOne('/api/seasons/current/calendar').flush({
        season: 2026,
        updatedAt: '2026-10-17T10:00:00Z',
        isStale: false,
        source: 'Jolpica',
        races: [],
      });
    });
    for (const kind of ['drivers', 'constructors']) {
      TestBed.inject(HttpTestingController)
        .expectOne('/api/seasons/current/standings/' + kind)
        .flush({
          season: 2026,
          round: null,
          updatedAt: '2026-10-17T10:00:00Z',
          isStale: false,
          source: 'Jolpica',
          kind,
          entries: [],
        });
    }
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(page.querySelector('.brand')?.textContent).toContain('F1 Race Lens');
    expect(page.querySelectorAll('.primary-nav .nav-item')).toHaveLength(8);
    expect(page.querySelector('.primary-nav a[href="/predictions"]')).not.toBeNull();
    expect(page.querySelector('.workspace')?.textContent).toContain('Календар ще не опубліковано');
    expect(document.activeElement).toBe(page.querySelector('#page-heading'));

    page.querySelector<HTMLButtonElement>('button.logout-button')!.click();
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/csrf').flush(null),
    );
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/logout').flush(null),
    );
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('app-login-form')).toBeTruthy();
    });
    await vi.waitFor(() => expect(document.activeElement).toBe(page.querySelector('#login')));
  });
});

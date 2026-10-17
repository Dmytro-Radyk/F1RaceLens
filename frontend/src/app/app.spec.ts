import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { App } from './app';

describe('Authentication screen', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
    }).compileComponents();
  });

  afterEach(() => {
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

    page.querySelector<HTMLButtonElement>('.auth-switch button')!.click();
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('Реєстрація');
    expect(page.querySelector('app-register-form form')).toBeTruthy();
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
    await fixture.whenStable();
    expect(page.querySelector('.form-error')?.textContent).toContain('Невірний');
    expect(page.querySelector('.app-shell')).toBeNull();
  });

  it('validates registration without creating a session', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.auth-switch button')!.click();
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(page.querySelectorAll('.field-error')).toHaveLength(2);

    const login = page.querySelector<HTMLInputElement>('#register-login')!;
    login.value = 'new-user';
    login.dispatchEvent(new Event('input', { bubbles: true }));
    const password = page.querySelector<HTMLInputElement>('#register-password')!;
    password.value = 'test-password';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(page.querySelector('.form-notice')?.textContent).toContain('підключення сервера');
    expect(page.querySelector('.app-shell')).toBeNull();
  });

  it('opens the existing application for the demo account', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const login = page.querySelector<HTMLInputElement>('#login')!;
    login.value = 'admin';
    login.dispatchEvent(new Event('input', { bubbles: true }));
    const password = page.querySelector<HTMLInputElement>('#login-password')!;
    password.value = '12345';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    page
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(page.querySelector('.brand')?.textContent).toContain('F1 Race Lens');
    expect(page.querySelectorAll('.primary-nav .nav-item')).toHaveLength(7);
    expect(page.querySelector('.workspace')?.textContent).toBe('');
    expect(document.activeElement).toBe(page.querySelector('#page-heading'));

    page.querySelector<HTMLButtonElement>('.logout-button')!.click();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(page.querySelector('app-login-form')).toBeTruthy();
    expect(document.activeElement).toBe(page.querySelector('#login'));
  });
});

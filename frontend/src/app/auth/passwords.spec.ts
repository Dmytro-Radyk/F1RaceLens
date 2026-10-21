import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import axe from 'axe-core';
import { PasswordForm } from './components/password-form/password-form';

describe('Password forms', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { fragment: 'user=test-user&token=test-token' } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup(mode: 'change' | 'forgot' | 'reset') {
    const fixture = TestBed.createComponent(PasswordForm);
    fixture.componentRef.setInput('mode', mode);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    function fill(id: string, value: string) {
      const input = page.querySelector<HTMLInputElement>('#' + id)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    }
    function submit() {
      page
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
    }
    return { fixture, page, fill, submit };
  }
  async function csrf() {
    await vi.waitFor(() => http.expectOne('/api/auth/csrf').flush(null));
  }
  it('blocks mismatched passwords and clears sensitive fields after the protected change', async () => {
    const { fixture, page, fill, submit } = setup('change');
    fill('current-password', 'old password test');
    fill('new-password', 'new password test');
    fill('confirm-password', 'mismatch');
    submit();
    expect(page.textContent).toContain('Паролі не збігаються.');
    http.expectNone('/api/auth/csrf');
    fill('confirm-password', 'new password test');
    submit();
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/change-password');
      expect(request.request.body).toEqual({
        currentPassword: 'old password test',
        newPassword: 'new password test',
      });
      request.flush(null);
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Пароль змінено.');
    });
    expect([...page.querySelectorAll('input')].every((input) => input.value === '')).toBe(true);
    expect(JSON.stringify(localStorage)).not.toMatch(/password test/);
    expect(
      (
        await axe.run(page, {
          rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
        })
      ).violations,
    ).toEqual([]);
  });
  it('shows delivery unavailability without claiming an email was sent', async () => {
    const { fixture, page, fill, submit } = setup('forgot');
    fill('recovery-email', 'invalid');
    submit();
    expect(page.textContent).toContain('Введіть коректну електронну пошту.');
    http.expectNone('/api/auth/csrf');
    fill('recovery-email', 'someone@example.test');
    submit();
    await csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/forgot-password')
        .flush({ code: 'recovery-unavailable' }, { status: 503, statusText: 'Unavailable' }),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Поштовий сервіс ще не налаштований');
    });
    expect(page.textContent).not.toContain('посилання буде надіслано');
    expect(page.querySelector<HTMLButtonElement>('button')!.disabled).toBe(false);
  });
  it('uses a link token only in the POST body and handles expired links', async () => {
    const { fixture, page, fill, submit } = setup('reset');
    fill('new-password', 'new password test');
    fill('confirm-password', 'new password test');
    submit();
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/reset-password');
      expect(request.request.body).toEqual({
        userId: 'test-user',
        token: 'test-token',
        newPassword: 'new password test',
      });
      expect(request.request.urlWithParams).not.toContain('test-token');
      request.flush({ code: 'invalid-reset' }, { status: 400, statusText: 'Invalid link' });
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Посилання недійсне');
    });
    expect(JSON.stringify(localStorage)).not.toContain('test-token');
    expect([...page.querySelectorAll('input')].every((input) => input.value === '')).toBe(true);
  });
  it('finishes a reset without automatically signing in or keeping passwords visible', async () => {
    const { fixture, page, fill, submit } = setup('reset');
    fill('new-password', 'new password test');
    fill('confirm-password', 'new password test');
    submit();
    await csrf();
    await vi.waitFor(() => http.expectOne('/api/auth/reset-password').flush(null));
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Пароль відновлено.');
    });
    expect(page.querySelector('form')).toBeNull();
    expect(page.querySelector('a[href="/login"]')).not.toBeNull();
    http.expectNone('/api/auth/login');
    http.expectNone('/api/auth/me');
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import axe from 'axe-core';
import { AuthService } from './services/auth.service';
import { EmailConfirmation } from './components/email-confirmation/email-confirmation';
import { EmailSettings } from '../features/profile/email-settings';

describe('Account email UI', () => {
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
          useValue: {
            snapshot: { fragment: 'user=one&token=test-token&email=new%40example.test' },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('updates the current account verification badge after confirmation', async () => {
    const auth = TestBed.inject(AuthService),
      restoring = auth.restore();
    http
      .expectOne('/api/auth/me')
      .flush({
        id: 'one',
        login: 'first-user',
        email: 'old@example.test',
        emailConfirmed: false,
        language: 'uk',
        theme: 'dark',
        driverIds: [],
        teamIds: [],
        favourites: [],
      });
    await restoring;
    const fixture = TestBed.createComponent(EmailConfirmation);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!.click();
    await csrf();
    await vi.waitFor(() => http.expectOne('/api/auth/confirm-email').flush(null));
    await vi.waitFor(() => expect(auth.user()?.emailConfirmed).toBe(true));
    expect(auth.user()?.email).toBe('old@example.test');
    http.expectNone('/api/auth/me');
  });
  async function csrf() {
    await vi.waitFor(() => http.expectOne('/api/auth/csrf').flush(null));
  }
  it('waits for explicit confirmation and submits the fragment token only in the POST body', async () => {
    const fixture = TestBed.createComponent(EmailConfirmation);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    http.expectNone('/api/auth/csrf');
    page.querySelector<HTMLButtonElement>('button')!.click();
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/confirm-email');
      expect(request.request.body).toEqual({
        userId: 'one',
        token: 'test-token',
        newEmail: undefined,
      });
      request.flush(null);
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Електронну пошту підтверджено.');
    });
    expect(page.querySelector('button')).toBeNull();
    http.expectNone('/api/auth/login');
    expect(JSON.stringify(localStorage)).not.toContain('test-token');
  });
  it('handles an expired email-change link without changing the account', async () => {
    const fixture = TestBed.createComponent(EmailConfirmation);
    fixture.componentRef.setInput('change', true);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('button')!.click();
    await csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/confirm-email-change');
      expect(request.request.body.newEmail).toBe('new@example.test');
      request.flush({ code: 'invalid-email-link' }, { status: 400, statusText: 'Invalid' });
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Посилання підтвердження недійсне');
    });
    expect(TestBed.inject(AuthService).user()).toBeNull();
  });
  it('validates email, keeps the current address and clears the password when delivery fails', async () => {
    const auth = TestBed.inject(AuthService),
      restoring = auth.restore();
    http.expectOne('/api/auth/me').flush({
      id: 'one',
      login: 'first-user',
      email: 'old@example.test',
      emailConfirmed: false,
      language: 'uk',
      theme: 'dark',
      driverIds: [],
      teamIds: [],
      favourites: [],
    });
    await restoring;
    const fixture = TestBed.createComponent(EmailSettings);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const fill = (id: string, value: string) => {
      const input = page.querySelector<HTMLInputElement>('#' + id)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    const submit = () =>
      page
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fill('new-email', 'invalid');
    submit();
    fixture.detectChanges();
    http.expectNone('/api/auth/csrf');
    fill('new-email', 'new@example.test');
    fill('email-current-password', 'test password');
    submit();
    await csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/request-email-change')
        .flush({ code: 'recovery-unavailable' }, { status: 503, statusText: 'Unavailable' }),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Поштовий сервіс ще не налаштований');
    });
    expect(auth.user()?.email).toBe('old@example.test');
    expect(page.querySelector<HTMLInputElement>('#email-current-password')!.value).toBe('');
    expect(
      (
        await axe.run(page, {
          rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
        })
      ).violations,
    ).toEqual([]);
  });
});

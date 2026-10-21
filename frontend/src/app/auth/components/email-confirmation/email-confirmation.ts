import { Component, inject, input, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, accountError } from '../../services/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

@Component({
  selector: 'app-email-confirmation',
  imports: [RouterLink, TranslatePipe],
  styleUrl: '../password-form/password-form.css',
  template: `
    <h1 id="auth-heading" tabindex="-1">
      {{ (change() ? 'Підтвердження нової пошти' : 'Підтвердження пошти') | translate }}
    </h1>
    @if (!done()) {
      <p>
        {{
          'Підтвердьте дію кнопкою нижче. Саме відкриття посилання нічого не змінює.' | translate
        }}
      </p>
      <button type="button" [disabled]="busy()" (click)="confirm()">
        {{ (busy() ? 'Зберігаємо…' : 'Підтвердити') | translate }}
      </button>
    }
    <p id="email-confirm-feedback" tabindex="-1" [attr.role]="failed() ? 'alert' : 'status'">
      {{ feedback() | translate }}
    </p>
    <p>
      <a routerLink="/login">{{ 'Увійти' | translate }}</a> ·
      <a routerLink="/profile">{{ 'Мій профіль' | translate }}</a>
    </p>
  `,
})
export class EmailConfirmation {
  readonly change = input(false);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly link = new URLSearchParams(this.route.snapshot.fragment ?? '');
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly feedback = signal('');
  protected readonly failed = signal(false);
  constructor() {
    const location = inject(Location);
    if (this.route.snapshot.fragment) location.replaceState(this.router.url.split('#')[0]);
  }
  protected async confirm(): Promise<void> {
    if (this.busy() || this.done()) return;
    const userId = this.link.get('user'),
      token = this.link.get('token'),
      email = this.link.get('email');
    if (!userId || !token || (this.change() && !email)) {
      this.failed.set(true);
      this.feedback.set(
        'Посилання підтвердження недійсне або прострочене. Запросіть нове у профілі.',
      );
      return;
    }
    this.busy.set(true);
    this.failed.set(false);
    this.feedback.set('');
    try {
      await this.auth.confirmEmail(userId, token, this.change() ? email! : undefined);
      this.done.set(true);
      this.link.delete('user');
      this.link.delete('token');
      this.link.delete('email');
      this.feedback.set(
        this.change()
          ? 'Пошту змінено й підтверджено. Увійдіть знову зі своїм логіном.'
          : 'Електронну пошту підтверджено.',
      );
      setTimeout(() => document.getElementById('email-confirm-feedback')?.focus());
    } catch (error) {
      this.failed.set(true);
      this.feedback.set(accountError(error));
    } finally {
      this.busy.set(false);
    }
  }
}

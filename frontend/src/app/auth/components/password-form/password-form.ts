import { Component, inject, input, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { email, form, FormField, maxLength, minLength, required } from '@angular/forms/signals';
import { AuthApiService } from '../../services/auth-api.service';
import { AuthService, accountError } from '../../services/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

@Component({
  selector: 'app-password-form',
  imports: [FormField, TranslatePipe, RouterLink],
  templateUrl: './password-form.html',
  styleUrl: './password-form.css',
})
export class PasswordForm {
  readonly mode = input.required<'change' | 'forgot' | 'reset'>();
  private readonly api = inject(AuthApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly recovery = new URLSearchParams(this.route.snapshot.fragment ?? '');
  protected readonly credentials = signal({ currentPassword: '', newPassword: '', confirm: '' });
  protected readonly passwords = form(this.credentials, (fields) => {
    required(fields.newPassword);
    minLength(fields.newPassword, 12);
    maxLength(fields.newPassword, 128);
    maxLength(fields.currentPassword, 128);
    required(fields.confirm);
    maxLength(fields.confirm, 128);
  });
  protected readonly address = signal({ email: '' });
  protected readonly recoveryForm = form(this.address, (fields) => {
    required(fields.email);
    email(fields.email);
    maxLength(fields.email, 254);
  });
  protected readonly busy = signal(false);
  protected readonly feedback = signal('');
  protected readonly failed = signal(false);
  protected readonly done = signal(false);
  protected readonly submitted = signal(false);
  constructor() {
    if (this.route.snapshot.fragment) this.location.replaceState(this.router.url.split('#')[0]);
  }
  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || this.done()) return;
    this.submitted.set(true);
    this.feedback.set('');
    this.failed.set(true);
    const { currentPassword, newPassword, confirm } = this.credentials();
    if (this.mode() === 'forgot') {
      if (this.recoveryForm().invalid()) {
        this.feedback.set('Введіть коректну електронну пошту.');
        return;
      }
    } else {
      if (this.passwords().invalid()) {
        this.feedback.set('Пароль: від 12 до 128 символів.');
        return;
      }
      if (newPassword !== confirm) {
        this.feedback.set('Паролі не збігаються.');
        return;
      }
      if (this.mode() === 'change' && !currentPassword) {
        this.feedback.set('Введіть поточний пароль.');
        return;
      }
      if (this.mode() === 'reset' && (!this.recovery.get('user') || !this.recovery.get('token'))) {
        this.feedback.set('Посилання недійсне або строк його дії минув. Запросіть нове.');
        return;
      }
    }
    this.busy.set(true);
    try {
      if (this.mode() === 'forgot') {
        await this.api.forgotPassword(this.address().email.trim());
        this.feedback.set(
          'Якщо обліковий запис із цією поштою існує, посилання буде надіслано. Перевірте також спам. Посилання діє 30 хвилин.',
        );
      } else if (this.mode() === 'change') {
        await this.auth.changePassword(currentPassword, newPassword);
        this.feedback.set('Пароль змінено. Інші сесії завершено.');
      } else {
        await this.auth.resetPassword(
          this.recovery.get('user')!,
          this.recovery.get('token')!,
          newPassword,
        );
        this.recovery.delete('user');
        this.recovery.delete('token');
        this.feedback.set('Пароль відновлено. Увійдіть із новим паролем.');
      }
      this.failed.set(false);
      this.done.set(this.mode() === 'reset');
      if (this.done()) setTimeout(() => document.getElementById('password-feedback')?.focus());
      this.submitted.set(false);
    } catch (error) {
      this.feedback.set(accountError(error));
    } finally {
      this.busy.set(false);
      this.credentials.set({ currentPassword: '', newPassword: '', confirm: '' });
    }
  }
}

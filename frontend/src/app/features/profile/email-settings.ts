import { Component, inject, signal } from '@angular/core';
import { email, form, FormField, maxLength, required } from '@angular/forms/signals';
import { AuthService, accountError } from '../../auth/services/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

@Component({
  selector: 'app-email-settings',
  imports: [FormField, TranslatePipe],
  templateUrl: './email-settings.html',
  styleUrl: '../../auth/components/password-form/password-form.css',
})
export class EmailSettings {
  protected readonly auth = inject(AuthService);
  protected readonly model = signal({ newEmail: '', currentPassword: '' });
  protected readonly fields = form(this.model, (fields) => {
    required(fields.newEmail);
    email(fields.newEmail);
    maxLength(fields.newEmail, 254);
    required(fields.currentPassword);
    maxLength(fields.currentPassword, 128);
  });
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly feedback = signal('');
  protected readonly failed = signal(false);
  protected async sendConfirmation(): Promise<void> {
    await this.perform(
      () => this.auth.requestEmailConfirmation(),
      'Запит прийнято. Перевірте пошту та спам. Посилання діє 30 хвилин.',
    );
  }
  protected async change(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.fields().invalid()) {
      this.failed.set(true);
      this.feedback.set('Перевірте нову пошту та поточний пароль.');
      return;
    }
    const { newEmail, currentPassword } = this.model();
    await this.perform(
      () => this.auth.requestEmailChange(newEmail.trim(), currentPassword),
      'Якщо нова адреса доступна, на неї надійде посилання. До підтвердження поточна пошта не зміниться.',
    );
    this.model.update((value) => ({ ...value, currentPassword: '' }));
    this.submitted.set(false);
  }
  private async perform(work: () => Promise<void>, success: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.feedback.set('');
    this.failed.set(false);
    try {
      await work();
      this.feedback.set(success);
    } catch (error) {
      this.failed.set(true);
      this.feedback.set(accountError(error));
    } finally {
      this.busy.set(false);
    }
  }
}

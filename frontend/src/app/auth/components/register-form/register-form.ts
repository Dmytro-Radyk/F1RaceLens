import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, inject, output, signal } from '@angular/core';
import {
  email,
  form,
  FormField,
  required,
  minLength,
  maxLength,
  pattern,
} from '@angular/forms/signals';
import { accountError, AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import type { RegistrationRequest } from '../../models/auth.types';

@Component({
  selector: 'app-register-form',
  imports: [TranslatePipe, FormField],
  templateUrl: './register-form.html',
})
export class RegisterForm {
  readonly loginRequested = output<void>();
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly model = signal<RegistrationRequest>({ login: '', email: '', password: '' });
  protected readonly registerForm = form(this.model, (fields) => {
    required(fields.login, { message: 'Введіть логін.' });
    required(fields.password, { message: 'Введіть пароль.' });
    required(fields.email, { message: 'Введіть електронну пошту.' });
    email(fields.email, { message: 'Введіть коректну електронну пошту.' });
    pattern(fields.login, /^[a-zA-Z0-9_-]{3,30}$/, {
      message: 'Логін: 3–30 латинських літер, цифр, _ або -.',
    });
    minLength(fields.password, 12, { message: 'Пароль: від 12 до 128 символів.' });
    maxLength(fields.password, 128, { message: 'Пароль: від 12 до 128 символів.' });
    maxLength(fields.email, 254, { message: 'Введіть коректну електронну пошту.' });
  });
  protected readonly notice = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) return;
    this.registerForm.login().markAsTouched();
    this.registerForm.password().markAsTouched();
    this.registerForm.email().markAsTouched();
    if (this.registerForm().invalid()) return;

    this.notice.set('');
    this.busy.set(true);
    try {
      await this.auth.register(this.model());
      await this.router.navigateByUrl('/profile');
    } catch (error) {
      this.notice.set(accountError(error));
    } finally {
      this.busy.set(false);
      this.model.update((value) => ({ ...value, password: '' }));
    }
  }
}

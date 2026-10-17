import { Component, inject, output, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { AuthService } from '../../services/auth.service';
import type { RegistrationRequest } from '../../models/auth.types';

@Component({
  selector: 'app-register-form',
  imports: [FormField],
  templateUrl: './register-form.html',
})
export class RegisterForm {
  readonly loginRequested = output<void>();
  private readonly auth = inject(AuthService);
  protected readonly model = signal<RegistrationRequest>({ login: '', password: '' });
  protected readonly registerForm = form(this.model, (fields) => {
    required(fields.login, { message: 'Введіть логін.' });
    required(fields.password, { message: 'Введіть пароль.' });
  });
  protected readonly notice = signal('');

  protected submit(event: Event): void {
    event.preventDefault();
    this.registerForm.login().markAsTouched();
    this.registerForm.password().markAsTouched();
    if (this.registerForm().invalid()) return;

    this.auth.register(this.model());
    this.notice.set('Реєстрація стане доступною після підключення сервера.');
  }
}

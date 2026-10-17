import { Component, inject, output, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { AuthService } from '../../services/auth.service';
import type { AuthCredentials } from '../../models/auth.types';
import { ActivatedRoute, Router } from '@angular/router';

@Component({
  selector: 'app-login-form',
  imports: [FormField],
  templateUrl: './login-form.html',
})
export class LoginForm {
  readonly registerRequested = output<void>();
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly model = signal<AuthCredentials>({ login: '', password: '' });
  protected readonly loginForm = form(this.model, (fields) => {
    required(fields.login, { message: 'Введіть логін.' });
    required(fields.password, { message: 'Введіть пароль.' });
  });
  protected readonly error = signal('');
  protected readonly notice = signal('');

  protected submit(event: Event): void {
    event.preventDefault();
    this.loginForm.login().markAsTouched();
    this.loginForm.password().markAsTouched();
    this.error.set('');
    if (this.loginForm().invalid()) return;

    if (this.auth.login(this.model())) {
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      void this.router.navigateByUrl(
        returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/calendar',
      );
    } else {
      this.error.set('Невірний логін або пароль.');
    }
  }

  protected showResetNotice(event: Event): void {
    event.preventDefault();
    this.notice.set('Скидання пароля стане доступним після підключення сервера.');
  }
}

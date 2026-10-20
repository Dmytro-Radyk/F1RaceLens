import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, inject, output, signal } from '@angular/core';
import { form, FormField, required, maxLength } from '@angular/forms/signals';
import { accountError, AuthService } from '../../services/auth.service';
import type { AuthCredentials } from '../../models/auth.types';
import { ActivatedRoute, Router } from '@angular/router';

@Component({
  selector: 'app-login-form',
  imports: [TranslatePipe, FormField],
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
    maxLength(fields.login, 254, { message: 'Перевірте логін, електронну пошту та пароль.' });
    maxLength(fields.password, 128, { message: 'Пароль: від 12 до 128 символів.' });
  });
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) return;
    this.loginForm.login().markAsTouched();
    this.loginForm.password().markAsTouched();
    this.error.set('');
    if (this.loginForm().invalid()) return;

    this.busy.set(true);
    try {
      await this.auth.login(this.model());
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      void this.router.navigateByUrl(
        returnUrl?.startsWith('/') && !returnUrl.startsWith('//') && !/[\\\r\n]/.test(returnUrl)
          ? returnUrl
          : '/home',
      );
    } catch (error) {
      this.error.set(accountError(error));
    } finally {
      this.busy.set(false);
      this.model.update((value) => ({ ...value, password: '' }));
    }
  }

  protected showResetNotice(event: Event): void {
    event.preventDefault();
    this.notice.set('Відновлення пароля через пошту ще не налаштоване.');
  }
}

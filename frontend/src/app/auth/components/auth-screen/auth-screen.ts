import { LanguagePicker } from '../../../core/i18n/language-picker';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, inject, signal } from '@angular/core';
import { LoginForm } from '../login-form/login-form';
import { RegisterForm } from '../register-form/register-form';
import { ThemeService } from '../../../core/services/theme.service';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PasswordForm } from '../password-form/password-form';
import { EmailConfirmation } from '../email-confirmation/email-confirmation';

@Component({
  selector: 'app-auth-screen',
  imports: [
    LanguagePicker,
    TranslatePipe,
    LoginForm,
    RegisterForm,
    RouterLink,
    PasswordForm,
    EmailConfirmation,
  ],
  templateUrl: './auth-screen.html',
  styleUrl: './auth-screen.css',
})
export class AuthScreen {
  protected readonly theme = inject(ThemeService);
  protected readonly mode = signal<
    'login' | 'register' | 'forgot' | 'reset' | 'confirm-email' | 'confirm-email-change'
  >(
    inject(ActivatedRoute).snapshot.data['emailMode'] ??
      inject(ActivatedRoute).snapshot.data['passwordMode'] ??
      'login',
  );
  protected switchTo(mode: 'login' | 'register'): void {
    this.mode.set(mode);
    setTimeout(() => document.getElementById('auth-heading')?.focus());
  }
}

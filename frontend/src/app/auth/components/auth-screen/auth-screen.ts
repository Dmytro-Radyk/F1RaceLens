import { Component, inject, signal } from '@angular/core';
import { LoginForm } from '../login-form/login-form';
import { RegisterForm } from '../register-form/register-form';
import { ThemeService } from '../../../core/services/theme.service';

@Component({
  selector: 'app-auth-screen',
  imports: [LoginForm, RegisterForm],
  templateUrl: './auth-screen.html',
  styleUrl: './auth-screen.css',
})
export class AuthScreen {
  protected readonly theme = inject(ThemeService);
  protected readonly mode = signal<'login' | 'register'>('login');
  protected switchTo(mode: 'login' | 'register'): void {
    this.mode.set(mode);
    setTimeout(() => document.getElementById('auth-heading')?.focus());
  }
}

import { Component, signal, ViewEncapsulation } from '@angular/core';
import { LoginForm } from '../login-form/login-form';
import { RegisterForm } from '../register-form/register-form';

@Component({
  selector: 'app-auth-screen',
  imports: [LoginForm, RegisterForm],
  templateUrl: './auth-screen.html',
  styleUrl: './auth-screen.css',
  encapsulation: ViewEncapsulation.None,
})
export class AuthScreen {
  protected readonly mode = signal<'login' | 'register'>('login');
  protected switchTo(mode: 'login' | 'register'): void {
    this.mode.set(mode);
    setTimeout(() => document.getElementById('auth-heading')?.focus());
  }
}

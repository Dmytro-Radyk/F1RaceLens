import { Injectable, signal } from '@angular/core';
import type { AuthCredentials, RegistrationRequest } from '../models/auth.types';

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly isAuthenticated = signal(false);

  login(credentials: AuthCredentials): boolean {
    const success = credentials.login === 'admin' && credentials.password === '12345';
    this.isAuthenticated.set(success);
    return success;
  }

  register(_request: RegistrationRequest): boolean {
    return false;
  }

  logout(): void {
    this.isAuthenticated.set(false);
  }
}

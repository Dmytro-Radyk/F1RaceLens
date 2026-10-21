import { inject, Service } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';
import type { Account, AuthCredentials, RegistrationRequest } from '../models/auth.types';

@Service()
export class AuthApiService {
  private readonly http = inject(HttpClient);
  me(): Promise<Account> {
    return firstValueFrom(this.http.get<Account>('/api/auth/me').pipe(timeout(8000)));
  }
  private async write<T>(method: 'POST' | 'PUT', path: string, body: unknown): Promise<T> {
    await firstValueFrom(this.http.get<void>('/api/auth/csrf').pipe(timeout(8000)));
    return firstValueFrom(
      this.http.request<T>(method, '/api/auth/' + path, { body }).pipe(timeout(12000)),
    );
  }
  login(credentials: AuthCredentials): Promise<Account> {
    return this.write('POST', 'login', credentials);
  }
  register(request: RegistrationRequest & Pick<Account, 'language' | 'theme'>): Promise<Account> {
    return this.write('POST', 'register', request);
  }
  logout(): Promise<void> {
    return this.write('POST', 'logout', {});
  }
  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return this.write('POST', 'change-password', { currentPassword, newPassword });
  }
  forgotPassword(email: string): Promise<void> {
    return this.write('POST', 'forgot-password', { email });
  }
  requestEmailConfirmation(): Promise<void> {
    return this.write('POST', 'request-email-confirmation', {});
  }
  requestEmailChange(newEmail: string, currentPassword: string): Promise<void> {
    return this.write('POST', 'request-email-change', { newEmail, currentPassword });
  }
  confirmEmail(userId: string, token: string, newEmail?: string): Promise<void> {
    return this.write('POST', newEmail === undefined ? 'confirm-email' : 'confirm-email-change', {
      userId,
      token,
      newEmail,
    });
  }
  resetPassword(userId: string, token: string, newPassword: string): Promise<void> {
    return this.write('POST', 'reset-password', { userId, token, newPassword });
  }
  preferences(language: Account['language'], theme: Account['theme']): Promise<Account> {
    return this.write('PUT', 'preferences', { language, theme });
  }
  favourites(driverIds: readonly string[], teamIds: readonly string[]): Promise<Account> {
    return this.write('PUT', 'favourites', { driverIds, teamIds });
  }
}

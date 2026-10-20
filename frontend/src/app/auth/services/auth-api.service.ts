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
  preferences(language: Account['language'], theme: Account['theme']): Promise<Account> {
    return this.write('PUT', 'preferences', { language, theme });
  }
  favourites(driverIds: readonly string[], teamIds: readonly string[]): Promise<Account> {
    return this.write('PUT', 'favourites', { driverIds, teamIds });
  }
}

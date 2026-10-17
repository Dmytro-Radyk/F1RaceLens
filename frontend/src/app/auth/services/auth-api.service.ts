import { Injectable } from '@angular/core';
import type { AuthCredentials, JwtTokens, RegistrationRequest } from '../models/auth.types';

/** Contract for the future backend adapter. No API endpoint is configured yet. */
@Injectable({ providedIn: 'root' })
export class AuthApiService {
  login(_credentials: AuthCredentials): Promise<JwtTokens> {
    return Promise.reject(new Error('Authorization API is not configured.'));
  }

  register(_request: RegistrationRequest): Promise<void> {
    return Promise.reject(new Error('Registration API is not configured.'));
  }
}

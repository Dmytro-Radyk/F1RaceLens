export interface AuthCredentials {
  login: string;
  password: string;
}

export interface RegistrationRequest extends AuthCredentials {
  email: string;
}

export interface Account {
  readonly id: string;
  readonly login: string;
  readonly email: string;
  readonly language: import('../../core/i18n/translations').Language;
  readonly theme: 'dark' | 'light';
  readonly driverIds: readonly string[];
  readonly teamIds: readonly string[];
  readonly favourites: readonly {
    readonly kind: 'driver' | 'team';
    readonly id: string;
    readonly name: string;
  }[];
}

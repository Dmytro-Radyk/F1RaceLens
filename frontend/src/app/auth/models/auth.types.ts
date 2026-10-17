export interface AuthCredentials {
  login: string;
  password: string;
}

export interface RegistrationRequest extends AuthCredentials {}

export interface JwtTokens {
  accessToken: string;
  refreshToken?: string;
}

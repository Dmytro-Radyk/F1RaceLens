import { computed, effect, inject, Service, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import type { Account, AuthCredentials, RegistrationRequest } from '../models/auth.types';
import { AuthApiService } from './auth-api.service';
import { LocaleService } from '../../core/i18n/locale.service';
import { ThemeService } from '../../core/services/theme.service';

export function accountError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 429) return 'Забагато спроб. Спробуйте пізніше.';
    if (error.status === 401) return 'Невірний логін або пароль, або вхід тимчасово заблоковано.';
    switch (error.error?.code) {
      case 'invalid-registration':
        return 'Перевірте логін, електронну пошту та пароль.';
      case 'registration-unavailable':
        return 'Не вдалося створити обліковий запис. Перевірте дані або виберіть інший логін і пошту.';
      case 'csrf':
        return 'Сеанс захисту оновився. Спробуйте ще раз.';
      case 'invalid-favourites':
        return 'Можна обрати до 20 пілотів і 20 команд.';
      case 'unknown-favourite':
        return 'Деякі обрані учасники недоступні. Оновіть сторінку.';
      case 'save-conflict':
        return 'Дані змінилися в іншому вікні. Оновіть сторінку та повторіть.';
    }
  }
  return 'Сервер тимчасово недоступний. Спробуйте ще раз.';
}

@Service()
export class AuthService {
  private readonly api = inject(AuthApiService);
  private readonly locale = inject(LocaleService);
  private readonly theme = inject(ThemeService);
  private readonly selected = signal<Account | null>(null);
  readonly user = this.selected.asReadonly();
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly preferenceError = signal('');
  private writes: Promise<unknown> = Promise.resolve();
  private restoring: Promise<void> | null = null;
  constructor() {
    effect((cleanup) => {
      const user = this.user();
      const language = this.locale.language();
      const theme = this.theme.mode();
      if (
        !user ||
        this.locale.loading() ||
        this.locale.failed() ||
        (user.language === language && user.theme === theme)
      )
        return;
      const timer = setTimeout(() => {
        void this.enqueue(async () => {
          if (this.user()?.id !== user.id) return;
          const account = await this.api.preferences(language, theme);
          if (this.user()?.id === account.id) this.selected.set(account);
          this.preferenceError.set('');
        }).catch((error) => {
          if (error instanceof HttpErrorResponse && error.status === 401) this.selected.set(null);
          this.preferenceError.set(
            error instanceof HttpErrorResponse && error.status === 401
              ? 'Сесія завершилася. Увійдіть знову.'
              : accountError(error),
          );
        });
      }, 400);
      cleanup(() => clearTimeout(timer));
    });
  }
  private enqueue<T>(work: () => Promise<T>): Promise<T> {
    const next = this.writes.then(work, work);
    this.writes = next.catch(() => undefined);
    return next;
  }
  restore(): Promise<void> {
    this.restoring ??= this.api
      .me()
      .then((user) => this.accept(user))
      .catch(() => {
        this.selected.set(null);
      });
    return this.restoring;
  }
  private async accept(user: Account): Promise<void> {
    await this.locale.setLanguage(user.language);
    this.theme.set(user.theme);
    this.selected.set(user);
    this.restoring = Promise.resolve();
    this.preferenceError.set('');
  }
  async login(credentials: AuthCredentials): Promise<void> {
    await this.enqueue(async () => this.accept(await this.api.login(credentials)));
  }
  async register(request: RegistrationRequest): Promise<void> {
    await this.enqueue(async () =>
      this.accept(
        await this.api.register({
          ...request,
          language: this.locale.language(),
          theme: this.theme.mode(),
        }),
      ),
    );
  }
  async logout(): Promise<void> {
    await this.enqueue(async () => {
      try {
        await this.api.logout();
      } catch (error) {
        if (!(error instanceof HttpErrorResponse && error.status === 401)) throw error;
      }
      this.selected.set(null);
      this.preferenceError.set('');
    });
  }
  async saveFavourites(driverIds: readonly string[], teamIds: readonly string[]): Promise<void> {
    await this.enqueue(async () => {
      try {
        this.selected.set(await this.api.favourites(driverIds, teamIds));
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 401) this.selected.set(null);
        throw error;
      }
    });
  }
}

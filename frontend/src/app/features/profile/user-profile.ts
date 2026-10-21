import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { AuthService, accountError } from '../../auth/services/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguagePicker } from '../../core/i18n/language-picker';
import { ThemeService } from '../../core/services/theme.service';
import type { StandingsResponse } from '../standings/standings.types';
import { PasswordForm } from '../../auth/components/password-form/password-form';
import { EmailSettings } from './email-settings';

@Component({
  selector: 'app-user-profile',
  imports: [TranslatePipe, RouterLink, LanguagePicker, PasswordForm, EmailSettings],
  templateUrl: './user-profile.html',
  styleUrl: './user-profile.css',
})
export class UserProfile {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly drivers = httpResource<StandingsResponse>(() =>
    this.auth.isAuthenticated() ? '/api/seasons/current/standings/drivers' : undefined,
  );
  protected readonly teams = httpResource<StandingsResponse>(() =>
    this.auth.isAuthenticated() ? '/api/seasons/current/standings/constructors' : undefined,
  );
  private readonly saved = computed(
    () => ({ drivers: this.auth.user()?.driverIds ?? [], teams: this.auth.user()?.teamIds ?? [] }),
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );
  protected readonly driverOptions = computed(() => this.options('driver'));
  protected readonly teamOptions = computed(() => this.options('team'));
  private options(kind: 'driver' | 'team') {
    const source = kind === 'driver' ? this.drivers : this.teams;
    const current = source.hasValue() ? source.value().entries : [];
    const archived = (this.auth.user()?.favourites ?? []).filter(
      (item) => item.kind === kind && !current.some((entry) => entry.id === item.id),
    );
    return [...current.map((item) => ({ id: item.id, name: item.name })), ...archived];
  }
  protected readonly chosen = linkedSignal(() => this.saved());
  protected readonly busy = signal(false);
  protected readonly feedback = signal('');
  protected readonly failed = signal(false);
  protected readonly dirty = computed(
    () =>
      JSON.stringify([...this.chosen().drivers].sort()) !==
        JSON.stringify([...this.saved().drivers].sort()) ||
      JSON.stringify([...this.chosen().teams].sort()) !==
        JSON.stringify([...this.saved().teams].sort()),
  );
  protected toggle(kind: 'drivers' | 'teams', id: string, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    this.chosen.update((value) => ({
      ...value,
      [kind]: checked
        ? [...new Set([...value[kind], id])]
        : value[kind].filter((item) => item !== id),
    }));
    this.feedback.set('');
  }
  protected async save(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.failed.set(false);
    this.feedback.set('');
    try {
      await this.auth.saveFavourites(this.chosen().drivers, this.chosen().teams);
      this.feedback.set('Обране збережено.');
    } catch (error) {
      this.failed.set(true);
      this.feedback.set(accountError(error));
    } finally {
      this.busy.set(false);
    }
  }
}

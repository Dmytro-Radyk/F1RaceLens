import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import type { ForecastScenario, PredictionResponse } from '../predictions.types';

@Component({
  selector: 'app-prediction-context',
  templateUrl: './prediction-context.html',
  styleUrl: '../season-predictions.css',
})
export class PredictionContext {
  readonly data = input.required<PredictionResponse>();
  readonly apply = output<ForecastScenario>();
  readonly reset = output<void>();
  protected readonly weather = linkedSignal(() => this.data().factors?.settings.weather ?? 'auto');
  protected readonly team = linkedSignal(
    () => this.data().factors?.settings.developmentTeamId ?? '',
  );
  protected readonly development = linkedSignal(
    () => this.data().factors?.settings.developmentPositions ?? 0,
  );
  protected readonly replacementEnabled = linkedSignal(
    () => !!this.data().factors?.settings.replacement,
  );
  protected readonly outgoing = linkedSignal(
    () => this.data().factors?.settings.replacement?.outgoingId ?? '',
  );
  protected readonly incoming = linkedSignal(
    () => this.data().factors?.settings.replacement?.incomingId ?? '',
  );
  protected readonly fromRound = linkedSignal(
    () =>
      this.data().factors?.settings.replacement?.fromRound ??
      this.data().factors?.events[0]?.round ??
      0,
  );
  protected readonly error = signal('');
  protected readonly outgoingOptions = computed(() =>
    this.data().drivers.filter((driver) => this.data().factors?.rosterIds.includes(driver.id)),
  );
  protected readonly incomingOptions = computed(() =>
    this.data().drivers.filter((driver) => !this.data().factors?.rosterIds.includes(driver.id)),
  );
  protected readonly rounds = computed(() => [
    ...new Set(this.data().factors?.events.map((item) => item.round) ?? []),
  ]);
  protected readonly gains = [-3, -2, -1, 0, 1, 2, 3];
  protected select(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }
  protected selectWeather(event: Event) {
    const value = this.select(event);
    if (value === 'auto' || value === 'dry' || value === 'wet') this.weather.set(value);
  }
  protected toggleReplacement(event: Event) {
    this.replacementEnabled.set((event.target as HTMLInputElement).checked);
  }
  protected submit() {
    this.error.set('');
    if (this.development() !== 0 && !this.team()) {
      this.error.set('Виберіть команду для поправки темпу.');
      return;
    }
    if (this.replacementEnabled() && (!this.outgoing() || !this.incoming())) {
      this.error.set('Виберіть обох пілотів для заміни.');
      return;
    }
    this.apply.emit({
      weather: this.weather(),
      developmentTeamId: this.development() === 0 ? null : this.team(),
      developmentPositions: this.development(),
      replacement: this.replacementEnabled()
        ? { outgoingId: this.outgoing(), incomingId: this.incoming(), fromRound: this.fromRound() }
        : null,
    });
  }
  protected readonly number = (value: number | null) =>
    value === null
      ? '—'
      : new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 1 }).format(value);
  protected readonly percent = (value: number | null) =>
    value === null ? 'Невідомо' : `${this.number(value * 100)}%`;
  protected readonly weatherLabel = (value: string) =>
    (
      ({
        forecast: 'Прогноз Open-Meteo',
        scenario: 'Заданий сценарій',
        'outside-horizon': 'Поза горизонтом прогнозу',
        'unknown-time': 'Немає часу сесії',
        'unknown-location': 'Немає координат',
      }) as Readonly<Record<string, string>>
    )[value] ?? 'Погода недоступна';
}

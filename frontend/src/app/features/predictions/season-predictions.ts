import { LocalDatePipe } from '../../shared/local-date.pipe';
import { currentLocale } from '../../core/i18n/locale.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { httpResource } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ForecastAccuracy } from './components/forecast-accuracy';
import { ForecastUpdateStatus } from './components/forecast-update-status';
import { SourceNote } from '../calendar/components/source-note';
import { PredictionTable, modelChance } from './prediction-table';
import type { ForecastRow, PredictionResponse, ForecastScenario } from './predictions.types';
import { PredictionContext } from './components/prediction-context';

@Component({
  selector: 'app-season-predictions',
  imports: [
    LocalDatePipe,
    TranslatePipe,
    RouterLink,
    SourceNote,
    PredictionTable,
    PredictionContext,
    ForecastAccuracy,
    ForecastUpdateStatus,
  ],
  templateUrl: './season-predictions.html',
  styleUrl: './season-predictions.css',
})
export class SeasonPredictions {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly engine = computed(() =>
    this.query().get('engine') === 'ml' ? 'ml' : 'script',
  );
  protected readonly showAccuracy = signal(false);
  protected readonly showUpdate = signal(false);
  protected selectEngine(engine: 'script' | 'ml') {
    this.scenario.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { engine },
      queryParamsHandling: 'merge',
    });
  }
  protected readonly scenario = signal<ForecastScenario | null>(null);
  protected readonly view = signal<'season' | 'race'>('season');
  protected readonly prediction = httpResource<PredictionResponse>(() =>
    this.engine() === 'ml'
      ? { url: '/api/predictions/ml' }
      : this.scenario()
        ? { url: '/api/predictions/scenario', method: 'POST', body: this.scenario() }
        : { url: '/api/predictions/current' },
  );
  protected readonly completed = computed(
    () => this.prediction.hasValue() && this.prediction.value().status === 'completed',
  );
  protected readonly races = computed(() =>
    this.prediction.hasValue() ? (this.prediction.value().races ?? []) : [],
  );
  protected readonly round = linkedSignal(() => this.races()[0]?.round ?? 0);
  protected readonly selectedRace = computed(() =>
    this.races().find((race) => race.round === this.round()),
  );
  protected readonly points = (value: number) =>
    new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 0 }).format(value);
  protected readonly number = (value: number) =>
    new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 1 }).format(value);
  protected readonly timestamp = (value: string) =>
    new Intl.DateTimeFormat(currentLocale(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  protected selectRound(event: Event) {
    this.round.set(Number((event.target as HTMLSelectElement).value));
  }
  protected readonly favourites = computed(() => {
    if (!this.prediction.hasValue()) return [];
    const data = this.prediction.value();
    const favourite = (rows: readonly ForecastRow[]) =>
      [...rows].sort((a, b) => b.titleChance - a.titleChance)[0];
    return [
      {
        label: this.completed() ? 'Перше місце серед пілотів' : 'Фаворит моделі серед пілотів',
        kind: 'drivers',
        entry: favourite(data.drivers),
      },
      {
        label: this.completed() ? 'Перше місце серед команд' : 'Фаворит моделі серед команд',
        kind: 'teams',
        entry: favourite(data.teams),
      },
    ].filter((item) => item.entry);
  });
  protected readonly chance = (value: number) => modelChance(value, this.completed());
  protected focusScenario() {
    document.getElementById('scenario-heading')?.focus();
  }
}

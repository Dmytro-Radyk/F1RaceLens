import { Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { createSeasonContext } from '../../shared/season-context';
import { SeasonPicker } from '../../shared/season-picker';
import { SourceNote } from '../calendar/components/source-note';
import type { StandingsResponse } from '../standings/standings.types';
import type { DriverProfileResponse } from '../drivers/models/driver-profile.types';
import { analyseRaces } from './analytics';
import { resultLabel } from '../compare/comparison';

@Component({
  selector: 'app-driver-analytics',
  imports: [RouterLink, SeasonPicker, SourceNote],
  templateUrl: './driver-analytics.html',
  styleUrl: './driver-analytics.css',
})
export class DriverAnalytics {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly season = createSeasonContext();
  protected readonly window = computed(() => (this.query().get('window') === '10' ? 10 : 5));
  protected readonly drivers = httpResource<StandingsResponse>(() =>
    this.season.valid() ? `/api/seasons/${this.season.season()}/standings/drivers` : undefined,
  );
  protected readonly driverId = computed(() => {
    const entries = this.drivers.hasValue() ? this.drivers.value().entries : [];
    const requested = this.query().get('driver');
    return requested === null
      ? (entries[0]?.id ?? '')
      : entries.some((entry) => entry.id === requested)
        ? requested
        : '';
  });
  protected readonly profile = httpResource<DriverProfileResponse>(() =>
    this.driverId() && this.drivers.hasValue()
      ? `/api/seasons/${this.drivers.value().season}/drivers/${this.driverId()}`
      : undefined,
  );
  protected readonly analytics = computed(() =>
    this.profile.hasValue() ? analyseRaces(this.profile.value().races, this.window()) : null,
  );
  protected select(key: 'driver' | 'window', event: Event) {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        driver: this.driverId() || null,
        [key]: (event.target as HTMLSelectElement).value || null,
      },
      queryParamsHandling: 'merge',
    });
  }
  protected readonly number = (value: number | null | undefined) =>
    value == null
      ? '—'
      : new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 2 }).format(value);
  protected readonly signed = (value: number | null) =>
    value === null ? '—' : `${value > 0 ? '+' : ''}${this.number(value)}`;
  protected readonly resultLabel = resultLabel;
}

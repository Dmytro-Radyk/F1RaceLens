import { currentLocale } from '../../core/i18n/locale.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, computed, inject, linkedSignal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { createSeasonContext } from '../../shared/season-context';
import { SeasonPicker } from '../../shared/season-picker';
import { SourceNote } from '../calendar/components/source-note';
import type { StandingsResponse } from '../standings/standings.types';
import type { DriverProfileResponse } from '../drivers/models/driver-profile.types';
import { compareRaces, resultLabel } from './comparison';
import { isFinish } from '../drivers/utils/form-chart';

@Component({
  selector: 'app-driver-comparison',
  imports: [TranslatePipe, RouterLink, SeasonPicker, SourceNote],
  templateUrl: './driver-comparison.html',
  styleUrl: './driver-comparison.css',
})
export class DriverComparison {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly season = createSeasonContext();
  protected readonly drivers = httpResource<StandingsResponse>(() =>
    this.season.valid() ? `/api/seasons/${this.season.season()}/standings/drivers` : undefined,
  );
  private selection(key: string, index: number) {
    const entries = this.drivers.hasValue() ? this.drivers.value().entries : [];
    const requested = this.query().get(key);
    return requested === null
      ? (entries[index]?.id ?? '')
      : entries.some((entry) => entry.id === requested)
        ? requested
        : '';
  }
  protected readonly first = computed(() => this.selection('first', 0));
  protected readonly second = computed(() => this.selection('second', 1));
  protected readonly pairValid = computed(
    () => !!this.first() && !!this.second() && this.first() !== this.second(),
  );
  protected readonly left = httpResource<DriverProfileResponse>(() =>
    this.pairValid() && this.drivers.hasValue()
      ? `/api/seasons/${this.drivers.value().season}/drivers/${this.first()}`
      : undefined,
  );
  protected readonly right = httpResource<DriverProfileResponse>(() =>
    this.pairValid() && this.drivers.hasValue()
      ? `/api/seasons/${this.drivers.value().season}/drivers/${this.second()}`
      : undefined,
  );
  protected readonly comparison = computed(() =>
    this.left.hasValue() && this.right.hasValue()
      ? compareRaces(this.left.value(), this.right.value())
      : null,
  );
  protected readonly activeRound = linkedSignal({
    source: this.comparison,
    computation: () => null as number | null,
  });
  protected readonly selectedRow = computed(() =>
    this.comparison()?.rows.find((row) => row.round === this.activeRound()),
  );
  protected readonly chart = computed(() => {
    const data = this.comparison();
    if (!data) return null;
    const maxRound = Math.max(1, ...data.rows.map((row) => row.round));
    const maxPosition = Math.max(
      10,
      ...data.rows.flatMap((row) =>
        [...row.left, ...row.right].map((race) => race.result.position),
      ),
    );
    const x = (round: number) => (maxRound === 1 ? 400 : 55 + ((round - 1) * 690) / (maxRound - 1));
    const y = (position: number) => 25 + ((position - 1) * 230) / (maxPosition - 1);
    const series = (side: 'left' | 'right') => {
      let path = '',
        open = false,
        previous = 0;
      const points: { x: number; y: number; round: number }[] = [];
      for (const row of data.rows) {
        const entries = row[side];
        const finished = entries.length === 1 && isFinish(entries[0]);
        if (!finished) {
          open = false;
          continue;
        }
        const point = { x: x(row.round), y: y(entries[0].result.position), round: row.round };
        path += `${open && row.round === previous + 1 ? 'L' : 'M'}${point.x},${point.y} `;
        points.push(point);
        open = true;
        previous = row.round;
      }
      return { path, points };
    };
    return {
      left: series('left'),
      right: series('right'),
      rounds: data.rows.map((row) => ({ round: row.round, x: x(row.round) })),
      ticks: [1, ...Array.from({ length: Math.floor(maxPosition / 5) }, (_, i) => (i + 1) * 5)].map(
        (value) => ({ value, y: y(value) }),
      ),
    };
  });
  protected select(key: 'first' | 'second', event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        first: this.first() || null,
        second: this.second() || null,
        [key]: value || null,
      },
      queryParamsHandling: 'merge',
    });
  }
  protected swap() {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { first: this.second(), second: this.first() },
      queryParamsHandling: 'merge',
    });
  }
  protected retry() {
    if (this.left.error()) this.left.reload();
    if (this.right.error()) this.right.reload();
  }
  protected readonly resultLabel = resultLabel;
  protected readonly number = (value: number | null | undefined) =>
    value == null
      ? '—'
      : new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 2 }).format(value);
}

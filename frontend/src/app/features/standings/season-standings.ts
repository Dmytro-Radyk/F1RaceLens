import { Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { SourceNote } from '../calendar/components/source-note';
import type { StandingsEntry, StandingsResponse } from './standings.types';

@Component({
  selector: 'app-season-standings',
  imports: [RouterLink, SourceNote],
  templateUrl: './season-standings.html',
  styleUrl: './season-standings.css',
})
export class SeasonStandings {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly currentYear = new Date(Date.now()).getUTCFullYear();
  protected readonly seasons = Array.from(
    { length: this.currentYear - 1950 + 1 },
    (_, index) => this.currentYear - index,
  );
  protected readonly season = computed(() => this.query().get('season') ?? 'current');
  protected readonly selectedYear = computed(() =>
    this.season() === 'current' ? this.currentYear : Number(this.season()),
  );
  protected readonly validSeason = computed(
    () =>
      this.season() === 'current' ||
      (/^\d{4}$/.test(this.season()) &&
        Number(this.season()) >= 1950 &&
        Number(this.season()) <= this.currentYear),
  );
  protected readonly kind = computed(() =>
    this.query().get('table') === 'constructors' ? 'constructors' : 'drivers',
  );
  protected readonly standings = httpResource<StandingsResponse>(() =>
    this.validSeason() ? `/api/seasons/${this.season()}/standings/${this.kind()}` : undefined,
  );
  protected readonly title = computed(() =>
    this.kind() === 'drivers' ? 'Залік пілотів' : 'Залік команд',
  );
  protected readonly tabs = [
    { id: 'drivers', label: 'Пілоти' },
    { id: 'constructors', label: 'Команди' },
  ] as const;
  private readonly numberFormat = new Intl.NumberFormat('uk-UA');
  protected readonly points = (value: number) => this.numberFormat.format(value);
  protected readonly positionLabel = (entry: StandingsEntry) => {
    const labels: Readonly<Record<string, string>> = {
      E: 'Виключено',
      D: 'Дискваліфіковано',
      '-': 'Поза заліком',
    };
    return labels[entry.positionText] ?? entry.position ?? entry.positionText;
  };
  protected readonly podium = (entry: StandingsEntry) =>
    entry.position !== null && entry.position <= 3 && /^\d+$/.test(entry.positionText);
  protected selectSeason(event: Event) {
    const year = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { season: year === String(this.currentYear) ? null : year },
      queryParamsHandling: 'merge',
    });
  }
}

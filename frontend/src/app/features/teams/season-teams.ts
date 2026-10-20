import { currentLocale } from '../../core/i18n/locale.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SourceNote } from '../calendar/components/source-note';
import type { StandingsEntry, StandingsResponse } from '../standings/standings.types';
import { createSeasonContext } from '../../shared/season-context';
import { SeasonPicker } from '../../shared/season-picker';
import { HistoricalTeams } from './historical-teams';

@Component({
  selector: 'app-season-teams',
  imports: [TranslatePipe, RouterLink, SourceNote, SeasonPicker, HistoricalTeams],
  templateUrl: './season-teams.html',
  styleUrl: './season-teams.css',
})
export class SeasonTeams {
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  protected readonly season = createSeasonContext();
  protected readonly historical = computed(() => this.season.year() < 1958);
  protected readonly teamId = computed(() => this.params().get('teamId'));
  protected readonly valid = computed(
    () =>
      this.season.valid() && (this.teamId() === null || /^[a-z0-9_-]{1,80}$/.test(this.teamId()!)),
  );
  protected readonly teams = httpResource<StandingsResponse>(() =>
    this.valid() && !this.historical()
      ? `/api/seasons/${this.season.season()}/standings/constructors`
      : undefined,
  );
  protected readonly team = computed(() =>
    this.teams.hasValue()
      ? this.teams.value().entries.find((entry) => entry.id === this.teamId())
      : undefined,
  );
  protected readonly drivers = httpResource<StandingsResponse>(() =>
    this.team() && this.teams.hasValue()
      ? `/api/seasons/${this.teams.value().season}/standings/drivers`
      : undefined,
  );
  protected readonly roster = computed(() => {
    const team = this.team();
    return team && this.drivers.hasValue()
      ? this.drivers.value().entries.filter((driver) => driver.teams.includes(team.name))
      : [];
  });
  private get numberFormat() {
    return new Intl.NumberFormat(currentLocale());
  }
  protected readonly points = (value: number) => this.numberFormat.format(value);
  protected readonly position = (entry: StandingsEntry) => {
    const labels: Readonly<Record<string, string>> = {
      E: 'Виключено',
      D: 'Дискваліфіковано',
      '-': 'Поза заліком',
    };
    return labels[entry.positionText] ?? entry.position ?? entry.positionText;
  };
}

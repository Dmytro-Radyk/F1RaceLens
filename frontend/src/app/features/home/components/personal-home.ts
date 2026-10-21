import { Component, computed, inject, input, output } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../auth/services/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { RaceStatus } from '../../../core/i18n/race-status';
import { currentLocale } from '../../../core/i18n/locale.service';
import { SourceNote } from '../../calendar/components/source-note';
import type { CalendarResponse, RaceSchedule } from '../../calendar/models/calendar.types';
import type { SessionResultsResponse } from '../../calendar/models/results.types';
import type { StandingsResponse } from '../../standings/standings.types';
import {
  formatScheduleDate,
  formatScheduleTime,
  isPastSchedule,
} from '../../calendar/utils/schedule-format';

@Component({
  selector: 'app-personal-home',
  host: { '[hidden]': '!auth.user()' },
  imports: [TranslatePipe, RouterLink, SourceNote],
  templateUrl: './personal-home.html',
  styleUrl: './personal-home.css',
})
export class PersonalHome {
  readonly calendar = input<CalendarResponse>();
  readonly drivers = input<StandingsResponse>();
  readonly teams = input<StandingsResponse>();
  readonly nextRace = input<RaceSchedule>();
  readonly loading = input(false);
  readonly failed = input(false);
  readonly retry = output<void>();
  protected readonly auth = inject(AuthService);
  protected readonly status = inject(RaceStatus);
  protected readonly season = computed(
    () => this.calendar()?.season ?? this.drivers()?.season ?? this.teams()?.season,
  );
  protected readonly favourites = computed(() => this.auth.user()?.favourites ?? []);
  protected readonly lastRace = computed(() => {
    const calendar = this.calendar(),
      standings = this.drivers();
    return calendar && standings?.season === calendar.season && standings.round
      ? calendar.races.find((race) => race.round === standings.round)
      : undefined;
  });
  protected readonly results = httpResource<SessionResultsResponse>(() => {
    const race = this.lastRace();
    return this.auth.user() && this.favourites().length && race
      ? `/api/seasons/${race.season}/races/${race.round}/results/race`
      : undefined;
  });
  protected readonly cards = computed(() =>
    this.favourites().map((favourite) => {
      const standings = favourite.kind === 'driver' ? this.drivers() : this.teams();
      const standing =
        standings && standings.season === this.season()
          ? standings.entries.find((row) => row.id === favourite.id)
          : undefined;
      const data = this.results.hasValue() ? this.results.value() : undefined;
      const race = this.lastRace();
      const entries =
        data && data.season === race?.season && data.round === race.round && data.kind === 'race'
          ? data.entries
          : [];
      const last =
        favourite.kind === 'driver' ? entries.filter((row) => row.driverId === favourite.id) : [];
      // The legacy result contract has team names, not constructor IDs. Only use an exact current-source name match.
      const teamResults =
        favourite.kind === 'team' && standing
          ? entries.filter((row) => row.team === standing.name)
          : [];
      const racePoints =
        teamResults.length && teamResults.every((row) => row.points !== null)
          ? teamResults.reduce((sum, row) => sum + row.points!, 0)
          : null;
      return { ...favourite, standing, last, racePoints };
    }),
  );
  protected readonly number = (value: number | null | undefined) =>
    value == null ? '—' : new Intl.NumberFormat(currentLocale()).format(value);
  protected readonly date = formatScheduleDate;
  protected readonly time = formatScheduleTime;
  protected readonly past = (race: RaceSchedule) => isPastSchedule(race, Date.now());
}

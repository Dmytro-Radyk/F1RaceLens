import { Component, computed, DestroyRef, inject, linkedSignal, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import type { CalendarResponse, RaceSchedule } from '../../models/calendar.types';
import { SourceNote } from '../../components/source-note';
import {
  countryName,
  formatScheduleDate,
  formatScheduleTime,
  isPastSchedule,
  scheduleLabel,
  userTimeZone,
} from '../../utils/schedule-format';

type CalendarFilter = 'all' | 'upcoming' | 'past';

@Component({
  selector: 'app-season-calendar',
  imports: [RouterLink, SourceNote],
  templateUrl: './season-calendar.html',
})
export class SeasonCalendar {
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
  protected readonly validSeason = computed(() => {
    const season = this.season();
    return (
      season === 'current' ||
      (/^\d{4}$/.test(season) && Number(season) >= 1950 && Number(season) <= this.currentYear)
    );
  });
  protected readonly selectedYear = computed(() =>
    this.season() === 'current' ? this.currentYear : Number(this.season()),
  );
  protected readonly calendar = httpResource<CalendarResponse>(() =>
    this.validSeason() ? `/api/seasons/${this.season()}/calendar` : undefined,
  );
  protected readonly filter = linkedSignal<string, CalendarFilter>({
    source: this.season,
    computation: () => 'all',
  });
  protected readonly now = signal(Date.now());
  protected readonly filters: readonly { id: CalendarFilter; label: string }[] = [
    { id: 'all', label: 'Усі етапи' },
    { id: 'upcoming', label: 'Майбутні' },
    { id: 'past', label: 'Минулі' },
  ];
  protected readonly races = computed(() =>
    this.calendar.hasValue() ? this.calendar.value().races : [],
  );
  protected readonly upcoming = computed(() =>
    this.races().filter((race) => !isPastSchedule(race, this.now())),
  );
  protected readonly nextRace = computed(() => this.upcoming()[0]);
  protected readonly visibleRaces = computed(() =>
    this.races().filter((race) => {
      const past = isPastSchedule(race, this.now());
      return this.filter() === 'all' || (this.filter() === 'past' ? past : !past);
    }),
  );
  protected readonly formatDate = formatScheduleDate;
  protected readonly formatTime = formatScheduleTime;
  protected readonly countryName = countryName;
  protected readonly scheduleLabel = scheduleLabel;
  protected readonly timeZone = userTimeZone;
  protected readonly isSprint = (race: RaceSchedule) =>
    race.sessions.some((session) => session.kind === 'Sprint');

  protected selectSeason(event: Event) {
    const year = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { season: year === String(this.currentYear) ? null : year },
      queryParamsHandling: 'merge',
    });
  }

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}

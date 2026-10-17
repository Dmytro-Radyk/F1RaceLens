import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
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
  protected readonly calendar = httpResource<CalendarResponse>(
    () => '/api/seasons/current/calendar',
  );
  protected readonly filter = signal<CalendarFilter>('all');
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

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}

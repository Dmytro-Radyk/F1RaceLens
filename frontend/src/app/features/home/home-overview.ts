import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import type { CalendarResponse, RaceSchedule } from '../calendar/models/calendar.types';
import type { StandingsResponse } from '../standings/standings.types';
import { StandingsSummary } from './components/standings-summary';
import { SourceNote } from '../calendar/components/source-note';
import {
  countryName,
  formatScheduleDate,
  formatScheduleTime,
  isPastSchedule,
} from '../calendar/utils/schedule-format';

export function nextHomeRace(
  calendar: CalendarResponse,
  standings: StandingsResponse | undefined,
  now: number,
): RaceSchedule | undefined {
  const ordered = [...calendar.races].sort((a, b) => a.round - b.round);
  if (standings?.season === calendar.season && standings.round !== null) {
    const through = standings.round;
    return ordered.find((race) => race.round > through);
  }
  return ordered.find((race) => !isPastSchedule(race, now));
}

@Component({
  selector: 'app-home-overview',
  imports: [RouterLink, SourceNote, StandingsSummary],
  templateUrl: './home-overview.html',
  styleUrl: './home-overview.css',
})
export class HomeOverview {
  protected readonly calendar = httpResource<CalendarResponse>(
    () => '/api/seasons/current/calendar',
  );
  protected readonly drivers = httpResource<StandingsResponse>(
    () => '/api/seasons/current/standings/drivers',
  );
  protected readonly teams = httpResource<StandingsResponse>(
    () => '/api/seasons/current/standings/constructors',
  );
  protected readonly now = signal(Date.now());
  protected readonly season = computed(() =>
    this.calendar.hasValue()
      ? this.calendar.value().season
      : this.drivers.hasValue()
        ? this.drivers.value().season
        : this.teams.hasValue()
          ? this.teams.value().season
          : undefined,
  );
  protected readonly nextRace = computed(() => {
    return this.calendar.hasValue()
      ? nextHomeRace(
          this.calendar.value(),
          this.drivers.hasValue() ? this.drivers.value() : undefined,
          this.now(),
        )
      : undefined;
  });
  protected readonly confirmed = computed(() => {
    if (!this.calendar.hasValue() || !this.drivers.hasValue()) return null;
    const calendar = this.calendar.value(),
      standings = this.drivers.value(),
      through = standings.round;
    if (standings.season !== calendar.season || through === null) return null;
    return calendar.races.filter((race) => race.round <= through).length;
  });
  protected readonly remaining = computed(() => {
    const confirmed = this.confirmed();
    return confirmed === null || !this.calendar.hasValue()
      ? null
      : this.calendar.value().races.length - confirmed;
  });
  protected readonly standingPanels = [
    { resource: this.drivers, title: 'Лідери серед пілотів', kind: 'drivers', table: 'drivers' },
    { resource: this.teams, title: 'Кубок конструкторів', kind: 'teams', table: 'constructors' },
  ] as const;
  protected readonly sections = [
    { path: '/calendar', title: 'Календар', text: 'Усі етапи, розклад сесій та результати.' },
    {
      path: '/compare',
      title: 'Порівняння',
      text: 'Форма, стабільність і результати двох пілотів.',
    },
    { path: '/insights', title: 'Аналітика', text: 'Тенденції та показники виступів за сезон.' },
    {
      path: '/predictions',
      title: 'Прогнози',
      text: 'Експериментальні оцінки статистичного скрипта.',
    },
  ] as const;
  protected readonly country = countryName;
  protected readonly date = formatScheduleDate;
  protected readonly time = formatScheduleTime;
  protected readonly past = (race: RaceSchedule) => isPastSchedule(race, this.now());

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}

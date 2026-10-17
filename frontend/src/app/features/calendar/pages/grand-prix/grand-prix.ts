import { Component, computed, inject } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import type { RaceResponse } from '../../models/calendar.types';
import { SourceNote } from '../../components/source-note';
import { ResultsTable } from '../../components/results-table';
import type { ResultKind } from '../../models/results.types';
import {
  countryName,
  formatScheduleDate,
  formatScheduleTime,
  formatSessionDay,
  userTimeZone,
} from '../../utils/schedule-format';

@Component({
  selector: 'app-grand-prix',
  imports: [RouterLink, SourceNote, ResultsTable],
  templateUrl: './grand-prix.html',
})
export class GrandPrix {
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly tabs = computed(() => {
    const items: { id: 'schedule' | ResultKind; label: string }[] = [
      { id: 'schedule', label: 'Розклад' },
      { id: 'qualifying', label: 'Кваліфікація' },
      { id: 'race', label: 'Гонка' },
    ];
    if (
      this.schedule.hasValue() &&
      this.schedule.value().race.sessions.some((session) => session.kind === 'Sprint')
    )
      items.push({ id: 'sprint', label: 'Спринт' });
    return items;
  });
  protected readonly selectedTab = computed(
    () => this.tabs().find((tab) => tab.id === this.query().get('session'))?.id ?? 'schedule',
  );
  protected readonly resultKind = computed(() => {
    const tab = this.selectedTab();
    return tab === 'schedule' ? null : tab;
  });
  protected readonly validParams = computed(() => {
    const season = this.params().get('season') ?? '';
    const round = this.params().get('round') ?? '';
    return (
      /^\d{4}$/.test(season) &&
      Number(season) >= 1950 &&
      Number(season) <= new Date().getFullYear() + 1 &&
      /^\d{1,3}$/.test(round) &&
      Number(round) >= 1 &&
      Number(round) <= 100
    );
  });
  protected readonly schedule = httpResource<RaceResponse>(() =>
    this.validParams()
      ? `/api/seasons/${this.params().get('season')}/races/${this.params().get('round')}`
      : undefined,
  );
  protected readonly notFound = computed(
    () =>
      !this.validParams() ||
      (this.schedule.error() instanceof HttpErrorResponse &&
        (this.schedule.error() as HttpErrorResponse).status === 404),
  );
  protected readonly calendarQuery = computed(() =>
    this.validParams() ? { season: this.params().get('season') } : {},
  );
  protected readonly formatDate = formatScheduleDate;
  protected readonly formatTime = formatScheduleTime;
  protected readonly formatDay = formatSessionDay;
  protected readonly countryName = countryName;
  protected readonly timeZone = userTimeZone;
}

import { currentLocale } from '../../../core/i18n/locale.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, computed, inject, input } from '@angular/core';
import { RaceStatus } from '../../../core/i18n/race-status';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import type { RaceSchedule } from '../models/calendar.types';
import type { ResultKind, SessionResultsResponse } from '../models/results.types';
import { SourceNote } from './source-note';
import { isPastSchedule } from '../utils/schedule-format';

@Component({
  selector: 'app-results-table',
  imports: [TranslatePipe, SourceNote, RouterLink],
  templateUrl: './results-table.html',
  styleUrl: './results-table.css',
})
export class ResultsTable {
  protected readonly status = inject(RaceStatus);
  readonly race = input.required<RaceSchedule>();
  readonly kind = input.required<ResultKind>();
  protected readonly results = httpResource<SessionResultsResponse>(
    () => `/api/seasons/${this.race().season}/races/${this.race().round}/results/${this.kind()}`,
  );
  protected readonly title = computed(
    () =>
      ({
        race: 'Результати гонки',
        qualifying: 'Результати кваліфікації',
        sprint: 'Результати спринту',
      })[this.kind()],
  );
  protected readonly isPast = computed(() => isPastSchedule(this.race(), Date.now()));
  protected readonly points = (value: number | null) =>
    value === null ? '—' : new Intl.NumberFormat(currentLocale()).format(value);
}

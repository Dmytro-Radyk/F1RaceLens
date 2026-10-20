import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, input } from '@angular/core';
import type { ScheduleMetadata } from '../models/calendar.types';
import { formatUpdatedAt, userTimeZone } from '../utils/schedule-format';

@Component({
  imports: [TranslatePipe],
  selector: 'app-source-note',
  template: `
    @if (metadata().isStale) {
      <p class="stale-notice" role="status">
        {{ staleMessage() | translate }}
      </p>
    }
    <footer class="source-note">
      <span
        >{{ 'Дані:' | translate }}
        <a href="https://api.jolpi.ca/">{{ metadata().source | translate }}</a></span
      >
      <span
        >{{ 'Оновлено' | translate }}
        <time [attr.datetime]="metadata().updatedAt">{{
          formatUpdatedAt(metadata().updatedAt) | translate
        }}</time></span
      >
      <span>{{ 'Часовий пояс: {0}' | translate: [timeZone] }}</span>
    </footer>
  `,
})
export class SourceNote {
  readonly metadata = input.required<ScheduleMetadata>();
  readonly staleMessage = input(
    'Джерело тимчасово недоступне. Показуємо останній збережений розклад.',
  );
  protected readonly formatUpdatedAt = formatUpdatedAt;
  protected readonly timeZone = userTimeZone;
}

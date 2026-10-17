import { Component, input } from '@angular/core';
import type { ScheduleMetadata } from '../models/calendar.types';
import { formatUpdatedAt, userTimeZone } from '../utils/schedule-format';

@Component({
  selector: 'app-source-note',
  template: `
    @if (metadata().isStale) {
      <p class="stale-notice" role="status">
        {{ staleMessage() }}
      </p>
    }
    <footer class="source-note">
      <span
        >Дані: <a href="https://api.jolpi.ca/">{{ metadata().source }}</a></span
      >
      <span
        >Оновлено
        <time [attr.datetime]="metadata().updatedAt">{{
          formatUpdatedAt(metadata().updatedAt)
        }}</time></span
      >
      <span>Часовий пояс: {{ timeZone }}</span>
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

import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, computed, input } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { SourceNote } from '../calendar/components/source-note';
import type { ScheduleMetadata } from '../calendar/models/calendar.types';

interface ParticipantsResponse extends ScheduleMetadata {
  readonly entries: readonly { readonly id: string; readonly name: string }[];
}

@Component({
  selector: 'app-historical-teams',
  imports: [TranslatePipe, RouterLink, SourceNote],
  templateUrl: './historical-teams.html',
  styleUrl: './season-teams.css',
})
export class HistoricalTeams {
  readonly year = input.required<number>();
  readonly teamId = input<string | null>(null);
  protected readonly teams = httpResource<ParticipantsResponse>(
    () => `/api/seasons/${this.year()}/participants/teams`,
  );
  protected readonly team = computed(() =>
    this.teams.hasValue()
      ? this.teams.value().entries.find((entry) => entry.id === this.teamId())
      : undefined,
  );
  protected readonly drivers = httpResource<ParticipantsResponse>(() =>
    this.team()
      ? `/api/seasons/${this.year()}/participants/teams/${this.teamId()}/drivers`
      : undefined,
  );
}

import { Component, computed, inject } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { createSeasonContext } from '../../../shared/season-context';
import { SeasonPicker } from '../../../shared/season-picker';
import { SourceNote } from '../../calendar/components/source-note';
import type { DriverProfileResponse } from '../models/driver-profile.types';
import { FormChart } from '../components/form-chart';
import type { StandingsEntry } from '../../standings/standings.types';

@Component({
  selector: 'app-driver-profile',
  imports: [RouterLink, SeasonPicker, SourceNote, FormChart],
  templateUrl: './driver-profile.html',
  styleUrl: './driver-profile.css',
})
export class DriverProfile {
  protected readonly season = createSeasonContext();
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  protected readonly id = computed(() => this.params().get('driverId') ?? '');
  protected readonly valid = computed(
    () =>
      this.season.valid() &&
      this.id().length >= 1 &&
      this.id().length <= 80 &&
      !/[^a-z0-9_-]/.test(this.id()),
  );
  protected readonly profile = httpResource<DriverProfileResponse>(() =>
    this.valid() ? `/api/seasons/${this.season.season()}/drivers/${this.id()}` : undefined,
  );
  protected readonly notFound = computed(
    () =>
      !this.valid() ||
      (this.profile.error() instanceof HttpErrorResponse &&
        (this.profile.error() as HttpErrorResponse).status === 404),
  );
  protected readonly points = (value: number | null) =>
    value === null ? '—' : new Intl.NumberFormat('uk-UA').format(value);
  protected readonly birthDate = (value: string | null) => {
    if (!value) return 'Не вказано';
    const [year, month, day] = value.split('-').map(Number);
    return new Intl.DateTimeFormat('uk-UA', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    }).format(new Date(year, month - 1, day, 12));
  };
  protected readonly position = (entry: StandingsEntry | null) => {
    if (!entry) return '—';
    return (
      (
        { E: 'Виключено', D: 'Дискваліфіковано', '-': 'Поза заліком' } as Readonly<
          Record<string, string>
        >
      )[entry.positionText] ??
      entry.position ??
      entry.positionText
    );
  };
}

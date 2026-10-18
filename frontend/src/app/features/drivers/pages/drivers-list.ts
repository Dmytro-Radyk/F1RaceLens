import { Component } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import type { StandingsResponse } from '../../standings/standings.types';
import { SourceNote } from '../../calendar/components/source-note';
import { createSeasonContext } from '../../../shared/season-context';
import { SeasonPicker } from '../../../shared/season-picker';

@Component({
  selector: 'app-drivers-list',
  imports: [RouterLink, SourceNote, SeasonPicker],
  templateUrl: './drivers-list.html',
  styleUrl: './driver-profile.css',
})
export class DriversList {
  protected readonly season = createSeasonContext();
  protected readonly drivers = httpResource<StandingsResponse>(() =>
    this.season.valid() ? `/api/seasons/${this.season.season()}/standings/drivers` : undefined,
  );
  protected readonly points = (value: number) => new Intl.NumberFormat('uk-UA').format(value);
}

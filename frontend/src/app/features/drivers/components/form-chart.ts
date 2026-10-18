import { Component, computed, input } from '@angular/core';
import type { DriverRace } from '../models/driver-profile.types';
import { buildFormChart } from '../utils/form-chart';

@Component({
  selector: 'app-form-chart',
  templateUrl: './form-chart.html',
  styleUrl: './form-chart.css',
})
export class FormChart {
  readonly races = input.required<readonly DriverRace[]>();
  protected readonly chart = computed(() => buildFormChart(this.races()));
}

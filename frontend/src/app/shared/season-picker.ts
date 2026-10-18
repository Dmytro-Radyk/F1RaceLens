import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-season-picker',
  template: `<div class="season-picker">
    <label for="driver-season">Сезон</label>
    <select id="driver-season" (change)="select($event)">
      @if (!valid()) {
        <option value="" selected disabled>Невідомий сезон</option>
      }
      @for (item of seasons; track item) {
        <option [value]="item" [selected]="item === year()">
          {{ item }}{{ item === currentYear ? ' · поточний' : '' }}
        </option>
      }
    </select>
  </div>`,
})
export class SeasonPicker {
  readonly year = input.required<number>();
  readonly valid = input(true);
  readonly yearChange = output<string>();
  protected readonly currentYear = new Date(Date.now()).getUTCFullYear();
  protected readonly seasons = Array.from(
    { length: this.currentYear - 1950 + 1 },
    (_, index) => this.currentYear - index,
  );
  protected select(event: Event) {
    this.yearChange.emit((event.target as HTMLSelectElement).value);
  }
}

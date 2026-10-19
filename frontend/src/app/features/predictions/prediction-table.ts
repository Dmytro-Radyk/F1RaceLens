import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { ForecastRow } from './predictions.types';

export function modelChance(value: number, completed: boolean): string {
  if (!completed && value < 0.1) return '<0,1%';
  if (!completed && value > 99.9) return '>99,9%';
  return `${new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 1 }).format(value)}%`;
}

@Component({
  selector: 'app-prediction-table',
  imports: [RouterLink],
  styleUrl: './season-predictions.css',
  template: `
    <div
      class="table-scroll"
      role="region"
      tabindex="0"
      [attr.aria-label]="kind() === 'drivers' ? 'Прогноз заліку пілотів' : 'Прогноз заліку команд'"
    >
      <table>
        <caption>
          {{
            completed()
              ? 'Залік за даними джерела'
              : 'Сортування за очікуваними підсумковими очками'
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col">{{ completed() ? 'Місце' : 'Прогноз місця' }}</th>
            <th scope="col">{{ kind() === 'drivers' ? 'Пілот' : 'Команда' }}</th>
            <th scope="col">Очікувані очки</th>
            <th scope="col">Діапазон очок*</th>
            <th scope="col">Діапазон місць*</th>
            <th scope="col">Титул**</th>
            <th scope="col">Топ-3**</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.id; let index = $index) {
            <tr>
              <td>{{ completed() ? row.currentPosition : index + 1 }}</td>
              <th scope="row">
                <a
                  [routerLink]="[kind() === 'drivers' ? '/drivers' : '/teams', row.id]"
                  [queryParams]="{ season: season() }"
                  >{{ row.name }}</a
                >
                @if (!row.active && !completed()) {
                  <span class="row-note">Без майбутніх нарахувань у цьому розрахунку</span>
                }
              </th>
              <td>{{ number(row.expectedPoints) }}</td>
              <td>{{ number(row.pointsLow) }}–{{ number(row.pointsHigh) }}</td>
              <td>{{ row.positionLow }}–{{ row.positionHigh }}</td>
              <td>
                <span class="chance-text">{{ chance(row.titleChance) }}</span
                ><span class="chance-track" aria-hidden="true"
                  ><span [style.width.%]="row.titleChance"></span
                ></span>
              </td>
              <td>{{ chance(row.top3Chance) }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class PredictionTable {
  readonly rows = input.required<readonly ForecastRow[]>();
  readonly season = input.required<number>();
  readonly kind = input.required<'drivers' | 'teams'>();
  readonly completed = input(false);
  protected readonly number = (value: number) =>
    new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 0 }).format(value);
  protected readonly chance = (value: number) => modelChance(value, this.completed());
}

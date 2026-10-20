import { currentLocale } from '../../core/i18n/locale.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { ForecastRow } from './predictions.types';

export function modelChance(value: number, completed: boolean): string {
  if (!completed && value < 0.1) return `<${new Intl.NumberFormat(currentLocale()).format(0.1)}%`;
  if (!completed && value > 99.9) return `>${new Intl.NumberFormat(currentLocale()).format(99.9)}%`;
  return `${new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 1 }).format(value)}%`;
}

@Component({
  selector: 'app-prediction-table',
  imports: [TranslatePipe, RouterLink],
  styleUrl: './season-predictions.css',
  template: `
    <div
      class="table-scroll"
      role="region"
      tabindex="0"
      [attr.aria-label]="
        (kind() === 'drivers' ? 'Прогноз заліку пілотів' : 'Прогноз заліку команд') | translate
      "
    >
      <table>
        <caption>
          {{
            (completed()
              ? 'Залік за даними джерела'
              : 'Сортування за очікуваними підсумковими очками'
            ) | translate
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col">{{ (completed() ? 'Місце' : 'Прогноз місця') | translate }}</th>
            <th scope="col">{{ (kind() === 'drivers' ? 'Пілот' : 'Команда') | translate }}</th>
            <th scope="col">{{ 'Очікувані очки' | translate }}</th>
            <th scope="col">{{ 'Діапазон очок*' | translate }}</th>
            <th scope="col">{{ 'Діапазон місць*' | translate }}</th>
            <th scope="col">{{ 'Титул**' | translate }}</th>
            <th scope="col">{{ 'Топ-3**' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.id; let index = $index) {
            <tr>
              <td>{{ (completed() ? row.currentPosition : index + 1) | translate }}</td>
              <th scope="row">
                <a
                  [routerLink]="[kind() === 'drivers' ? '/drivers' : '/teams', row.id]"
                  [queryParams]="{ season: season() }"
                  >{{ row.name | translate }}</a
                >
                @if (!row.active && !completed()) {
                  <span class="row-note">{{
                    'Без майбутніх нарахувань у цьому розрахунку' | translate
                  }}</span>
                }
              </th>
              <td>{{ number(row.expectedPoints) | translate }}</td>
              <td>
                {{ number(row.pointsLow) | translate }}–{{ number(row.pointsHigh) | translate }}
              </td>
              <td>{{ row.positionLow | translate }}–{{ row.positionHigh | translate }}</td>
              <td>
                <span class="chance-text">{{ chance(row.titleChance) | translate }}</span
                ><span class="chance-track" aria-hidden="true"
                  ><span [style.width.%]="row.titleChance"></span
                ></span>
              </td>
              <td>{{ chance(row.top3Chance) | translate }}</td>
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
    new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 0 }).format(value);
  protected readonly chance = (value: number) => modelChance(value, this.completed());
}

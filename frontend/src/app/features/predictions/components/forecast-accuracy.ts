import { Component } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { currentLocale } from '../../../core/i18n/locale.service';

interface AccuracyRow {
  readonly engine: string;
  readonly version: string;
  readonly round: number;
  readonly throughRound: number;
  readonly name: string;
  readonly status: string;
  readonly rankMae: number | null;
  readonly winnerBrier: number | null;
  readonly winnerHit: boolean | null;
}

@Component({
  selector: 'app-forecast-accuracy',
  imports: [TranslatePipe],
  template: `
    <section class="panel">
      <h2>{{ 'Перевірка точності' | translate }}</h2>
      <p>
        {{
          'Порівнюйте однакові етапи та зрізи. MAE — середня помилка порядку за очікуваними очками; Brier — помилка шансів перемоги. Менше — краще. Це перевірка гонок, а не шансів на титул.'
            | translate
        }}
      </p>
      @if (accuracy.isLoading()) {
        <p role="status">{{ 'Завантаження…' | translate }}</p>
      } @else if (accuracy.error()) {
        <p role="alert">{{ 'Журнал точності тимчасово недоступний.' | translate }}</p>
        <button type="button" (click)="accuracy.reload()">
          {{ 'Спробувати ще раз' | translate }}
        </button>
      } @else if (accuracy.hasValue()) {
        <button type="button" class="secondary-action" (click)="accuracy.reload()">
          {{ 'Перевірити оновлення' | translate }}
        </button>
        @if (!accuracy.value().rows.length) {
          <p>{{ 'Поки немає зафіксованих прогнозів для оцінювання.' | translate }}</p>
        } @else {
          <div
            class="table-scroll"
            role="region"
            tabindex="0"
            [attr.aria-label]="'Результати перевірки прогнозів' | translate"
          >
            <table>
              <caption>
                {{
                  'Прогнози, зафіксовані до старту гонки' | translate
                }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">{{ 'Модель' | translate }}</th>
                  <th scope="col">{{ 'Гран-прі' | translate }}</th>
                  <th scope="col">{{ 'Зріз після етапу' | translate }}</th>
                  <th scope="col">{{ 'Статус' | translate }}</th>
                  <th scope="col">MAE</th>
                  <th scope="col">Brier</th>
                  <th scope="col">{{ 'Переможця вгадано' | translate }}</th>
                </tr>
              </thead>
              <tbody>
                @for (
                  row of accuracy.value().rows;
                  track row.engine + row.version + row.round + ':' + row.throughRound
                ) {
                  <tr>
                    <th scope="row">
                      {{
                        (row.engine === 'ml' ? 'ML-модель · LightGBM v4' : 'Статистичний скрипт')
                          | translate
                      }}<br />{{ row.version }}
                    </th>
                    <td>{{ row.name }}</td>
                    <td>{{ row.throughRound }}</td>
                    <td>{{ status(row.status) | translate }}</td>
                    <td>{{ number(row.rankMae) }}</td>
                    <td>{{ number(row.winnerBrier) }}</td>
                    <td>
                      {{
                        (row.winnerHit === null ? '—' : row.winnerHit ? 'Так' : 'Ні') | translate
                      }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
  `,
  styles: `
    .panel {
      border: 1px solid var(--border);
      background: var(--panel-bg);
      border-radius: 12px;
      padding: 24px;
      min-width: 0;
    }
    h2 {
      font-size: 22px;
      margin-top: 0;
    }
    p {
      font-size: 13px;
      line-height: 1.8;
    }
    :host {
      display: block;
      min-width: 0;
    }
    .table-scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }
    th,
    td {
      text-align: start;
      padding: 12px;
      border-bottom: 1px solid var(--field-border);
    }
    caption {
      text-align: start;
      padding: 12px 0;
    }
    button {
      min-height: 44px;
      border: 1px solid var(--field-border);
      border-radius: 8px;
      padding: 10px 14px;
      background: var(--panel-bg);
      color: var(--text);
      font: inherit;
      cursor: pointer;
    }
  `,
})
export class ForecastAccuracy {
  protected readonly accuracy = httpResource<{ readonly rows: readonly AccuracyRow[] }>(
    () => '/api/predictions/accuracy',
  );
  protected number(value: number | null) {
    return value === null
      ? '—'
      : new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: 3 }).format(value);
  }
  protected status(value: string) {
    switch (value) {
      case 'scored':
        return 'Оцінено';
      case 'roster-mismatch':
        return 'Склад або класифікація не збігається';
      case 'awaiting-results':
        return 'Очікуємо офіційні результати';
      default:
        return 'Очікуємо гонку';
    }
  }
}

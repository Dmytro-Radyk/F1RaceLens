import { Component } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { currentLocale } from '../../../core/i18n/locale.service';

interface UpdateStatus {
  readonly error?: string | null;
  readonly state: string;
  readonly stage: string | null;
  readonly lastSuccessfulAt: string | null;
}

@Component({
  selector: 'app-forecast-update-status',
  imports: [TranslatePipe],
  template: `
    <section aria-labelledby="update-status-heading">
      <h2 id="update-status-heading">{{ 'Оновлення даних' | translate }}</h2>
      <p>{{ 'Перевірка стану не запускає оновлення.' | translate }}</p>
      @if (status.isLoading()) {
        <p role="status">{{ 'Завантаження…' | translate }}</p>
      } @else if (status.error()) {
        <p role="alert">{{ 'Статус оновлення недоступний' | translate }}</p>
      } @else if (status.hasValue()) {
        @let value = status.value();
        <p role="status">
          <strong>{{ state(value.state) | translate }}</strong>
        </p>
        @if (value.lastSuccessfulAt) {
          <p>{{ 'Останнє успішне оновлення: {0}' | translate: [date(value.lastSuccessfulAt)] }}</p>
        }
        @if (value.state === 'running') {
          <p>{{ stage(value.stage) | translate }}</p>
        }
        @if (value.state === 'failed' || value.state === 'interrupted') {
          @if (value.error === 'source-busy') {
            <p>
              {{ 'Джерело тимчасово обмежило запити. Повторіть оновлення пізніше.' | translate }}
            </p>
          }
          <p>
            {{
              'Оновлення завершилося помилкою. Раніше опубліковані прогнози збережено.' | translate
            }}
          </p>
        }
      }
      <button type="button" (click)="status.reload()">
        {{ 'Перевірити оновлення' | translate }}
      </button>
    </section>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    section {
      border: 1px solid var(--border);
      background: var(--panel-bg);
      border-radius: 12px;
      padding: 24px;
    }
    h2 {
      font-size: 22px;
      margin-top: 0;
    }
    p {
      font-size: 13px;
      line-height: 1.8;
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
export class ForecastUpdateStatus {
  protected readonly status = httpResource<UpdateStatus>(() => '/api/predictions/update-status');
  protected date(value: string) {
    return new Intl.DateTimeFormat(currentLocale(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
  protected state(value: string) {
    switch (value) {
      case 'never':
        return 'Оновлення ще не виконувалось';
      case 'running':
        return 'Оновлення триває';
      case 'succeeded':
        return 'Оновлення виконано';
      case 'failed':
        return 'Помилка оновлення';
      case 'interrupted':
        return 'Оновлення перервано';
      default:
        return 'Статус оновлення недоступний';
    }
  }
  protected stage(value: string | null) {
    switch (value) {
      case 'data':
        return 'Оновлення спортивних даних';
      case 'ml':
        return 'Розрахунок ML';
      case 'publish':
        return 'Збереження прогнозів і перевірка точності';
      default:
        return 'Перевірка готовності';
    }
  }
}

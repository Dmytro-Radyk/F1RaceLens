import { currentLocale } from '../../../core/i18n/locale.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SourceNote } from '../../calendar/components/source-note';
import type { StandingsResponse } from '../../standings/standings.types';

@Component({
  selector: 'app-standings-summary',
  imports: [TranslatePipe, RouterLink, SourceNote],
  styleUrl: './standings-summary.css',
  template: `
    <section [attr.aria-label]="title() | translate">
      <div class="card-heading">
        <h3>{{ title() | translate }}</h3>
        <span class="section-eyebrow">{{ 'ЗАЛІК' | translate }}</span>
      </div>
      @if (loading()) {
        <p role="status">{{ 'Завантажуємо залік…' | translate }}</p>
      } @else if (failed()) {
        <div role="alert">
          <p>{{ 'Залік тимчасово недоступний.' | translate }}</p>
          <button type="button" class="secondary-action" (click)="retry.emit()">
            {{ 'Оновити залік' | translate }}
          </button>
        </div>
      } @else if (data(); as standings) {
        <p class="note">
          {{
            'Сезон {0} · {1}'
              | translate
                : [
                    standings.season,
                    standings.round === null
                      ? 'Етап ще не вказаний'
                      : 'Після етапу ' + standings.round,
                  ]
          }}
        </p>
        @if (standings.entries.length) {
          <ol class="leaders-list">
            @for (entry of standings.entries.slice(0, 5); track entry.id) {
              <li>
                <span class="leader-position">{{
                  entry.position ?? entry.positionText | translate
                }}</span
                ><a
                  [routerLink]="['/' + kind(), entry.id]"
                  [queryParams]="{ season: standings.season }"
                  >{{ entry.name | translate }}
                  @if (kind() === 'drivers') {
                    <small>{{ entry.teams.join(' · ') | translate }}</small>
                  }</a
                ><span class="leader-points"
                  >{{ points(entry.points) | translate
                  }}<small>{{ 'очок' | translate }}</small></span
                >
              </li>
            }
          </ol>
        } @else {
          <p>{{ 'Залік сезону ще не опубліковано.' | translate }}</p>
        }
        <a
          class="text-link"
          routerLink="/standings"
          [queryParams]="{
            season: standings.season,
            table: kind() === 'drivers' ? 'drivers' : 'constructors',
          }"
          >{{ 'Повна таблиця' | translate }} <span aria-hidden="true">→</span></a
        >
        <app-source-note
          [metadata]="standings"
          staleMessage="Залік тимчасово не оновлюється. Показуємо збережені дані."
        />
      }
    </section>
  `,
})
export class StandingsSummary {
  readonly data = input<StandingsResponse>();
  readonly loading = input(false);
  readonly failed = input(false);
  readonly title = input.required<string>();
  readonly kind = input.required<'drivers' | 'teams'>();
  readonly retry = output<void>();
  protected readonly points = (value: number) =>
    new Intl.NumberFormat(currentLocale()).format(value);
}

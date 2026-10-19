import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SourceNote } from '../../calendar/components/source-note';
import type { StandingsResponse } from '../../standings/standings.types';

@Component({
  selector: 'app-standings-summary',
  imports: [RouterLink, SourceNote],
  styleUrl: './standings-summary.css',
  template: `
    <section [attr.aria-label]="title()">
      <div class="card-heading">
        <h3>{{ title() }}</h3>
        <span class="section-eyebrow">ЗАЛІК</span>
      </div>
      @if (loading()) {
        <p role="status">Завантажуємо залік…</p>
      } @else if (failed()) {
        <div role="alert">
          <p>Залік тимчасово недоступний.</p>
          <button type="button" class="secondary-action" (click)="retry.emit()">
            Оновити залік
          </button>
        </div>
      } @else if (data(); as standings) {
        <p class="note">
          Сезон {{ standings.season }} ·
          {{ standings.round === null ? 'Етап ще не вказаний' : 'Після етапу ' + standings.round }}
        </p>
        @if (standings.entries.length) {
          <ol class="leaders-list">
            @for (entry of standings.entries.slice(0, 5); track entry.id) {
              <li>
                <span class="leader-position">{{ entry.position ?? entry.positionText }}</span
                ><a
                  [routerLink]="['/' + kind(), entry.id]"
                  [queryParams]="{ season: standings.season }"
                  >{{ entry.name }}
                  @if (kind() === 'drivers') {
                    <small>{{ entry.teams.join(' · ') }}</small>
                  }</a
                ><span class="leader-points">{{ points(entry.points) }}<small>очок</small></span>
              </li>
            }
          </ol>
        } @else {
          <p>Залік сезону ще не опубліковано.</p>
        }
        <a
          class="text-link"
          routerLink="/standings"
          [queryParams]="{
            season: standings.season,
            table: kind() === 'drivers' ? 'drivers' : 'constructors',
          }"
          >Повна таблиця <span aria-hidden="true">→</span></a
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
  protected readonly points = (value: number) => new Intl.NumberFormat('uk-UA').format(value);
}

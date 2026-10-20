import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  selector: 'app-section-placeholder',
  imports: [TranslatePipe, RouterLink],
  template: `
    <section class="schedule-page feedback-panel">
      <p class="section-eyebrow">F1 RACE LENS</p>
      <h2>{{ (notFound ? 'Такої сторінки немає' : 'Розділ готується') | translate }}</h2>
      <p>
        {{
          (notFound
            ? 'Перевірте адресу або поверніться до календаря.'
            : 'А поки перегляньте календар сезону й розклад найближчих перегонів.'
          ) | translate
        }}
      </p>
      <a class="primary-link" routerLink="/calendar"
        >{{ 'До календаря' | translate }} <span aria-hidden="true">↗</span></a
      >
    </section>
  `,
})
export class SectionPlaceholder {
  protected readonly notFound = inject(ActivatedRoute).snapshot.data['notFound'] === true;
}

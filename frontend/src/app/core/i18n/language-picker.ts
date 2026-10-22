import { Component, inject } from '@angular/core';
import { LocaleService } from './locale.service';
import { TranslatePipe } from './translate.pipe';

@Component({
  selector: 'app-language-picker',
  imports: [TranslatePipe],
  template: `
    <label>
      <span class="language-label">{{ 'Мова' | translate }}</span>
      <select
        [attr.aria-busy]="locale.loading()"
        [value]="locale.language()"
        (change)="changeLanguage($event)"
      >
        @for (language of locale.languages; track language.id) {
          <option
            [value]="language.id"
            [selected]="language.id === locale.language()"
            [attr.lang]="language.id"
          >
            {{ language.name }}
          </option>
        }
      </select>
    </label>
    @if (locale.failed()) {
      <small role="status">{{
        'Не вдалося завантажити мову. Спробуйте ще раз.' | translate
      }}</small>
    }
  `,
  styles: `
    :host {
      display: inline-block;
      min-width: 0;
    }
    label {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      color: var(--muted);
      font-size: 0.8rem;
    }
    select {
      min-height: 44px;
      min-width: 0;
      max-width: 160px;
      padding: 0.45rem 2.5rem 0.45rem 0.65rem;
      color: var(--text);
      background-color: var(--field-bg);
      border: 1px solid var(--field-border);
      border-radius: 8px;
      font: inherit;
      cursor: pointer;
    }
    select:focus-visible {
      outline: 2px solid #e43242;
      outline-offset: 3px;
    }
    @media (max-width: 600px) {
      .language-label {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
      }
      select {
        max-width: 160px;
        width: 100%;
      }
    }
  `,
})
export class LanguagePicker {
  protected readonly locale = inject(LocaleService);
  protected changeLanguage(event: Event): void {
    if (event.target instanceof HTMLSelectElement) void this.locale.setLanguage(event.target.value);
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LocaleService } from '../../core/i18n/locale.service';
import { CareerCatalog, matchesCareer, searchText } from '../catalog/catalog.service';
import { NewsService } from '../news/news.service';
import type { CalendarResponse } from '../calendar/models/calendar.types';
import { historyChapters } from '../history/f1-history';

const pages = [
  ['home', 'Головна'],
  ['news', 'Новини F1'],
  ['calendar', 'Календар'],
  ['standings', 'Турнірна таблиця'],
  ['drivers', 'Пілоти'],
  ['teams', 'Команди'],
  ['compare', 'Порівняння'],
  ['insights', 'Аналітика'],
  ['predictions', 'Прогнози'],
  ['history', 'Історія F1'],
  ['profile', 'Мій профіль'],
] as const;
@Component({
  selector: 'app-site-search',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './site-search.html',
  styleUrl: '../catalog/catalog.css',
})
export class SiteSearch {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly locale = inject(LocaleService);
  protected readonly query = signal(
    this.route.snapshot.queryParamMap.get('q')?.slice(0, 100) ?? '',
  );
  protected readonly submitted = signal(this.query().trim());
  protected readonly history = computed(() =>
    historyChapters.filter((chapter) =>
      searchText(
        `${chapter.year} ${this.locale.text(chapter.title)} ${this.locale.text(chapter.text)}`,
      ).includes(searchText(this.submitted())),
    ),
  );
  protected readonly catalog = inject(CareerCatalog).data;
  constructor() {
    inject(CareerCatalog).load();
  }
  protected readonly news = inject(NewsService).feed;
  protected readonly year = computed(() => {
    const y = /\b(19\d{2}|20\d{2})\b/.exec(this.submitted())?.[0];
    return y && +y >= 1950 && +y <= new Date().getUTCFullYear() ? y : 'current';
  });
  protected readonly calendar = httpResource<CalendarResponse>(() =>
    this.submitted().length >= 2 ? `/api/seasons/${this.year()}/calendar` : undefined,
  );
  protected readonly sections = computed(() =>
    pages.filter(([, label]) =>
      searchText(this.locale.text(label)).includes(searchText(this.submitted())),
    ),
  );
  protected readonly drivers = computed(() =>
    this.catalog.hasValue()
      ? this.catalog.value().drivers.filter((row) => matchesCareer(row, this.submitted()))
      : [],
  );
  protected readonly teams = computed(() =>
    this.catalog.hasValue()
      ? this.catalog.value().constructors.filter((row) => matchesCareer(row, this.submitted()))
      : [],
  );
  protected readonly articles = computed(() =>
    this.news.hasValue()
      ? this.news
          .value()
          .articles.filter((row) => searchText(row.title).includes(searchText(this.submitted())))
      : [],
  );
  protected readonly races = computed(() =>
    this.calendar.hasValue()
      ? this.calendar.value().races.filter((row) => {
          const text = searchText(
            `${row.name} ${row.circuit.name} ${row.circuit.locality} ${row.circuit.country} ${row.season}`,
          );
          return searchText(this.submitted())
            .split(' ')
            .every((word) => text.includes(word));
        })
      : [],
  );
  protected update(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }
  protected search(event: Event): void {
    event.preventDefault();
    this.submitted.set(this.query().trim());
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: this.submitted() || null },
      replaceUrl: true,
    });
  }
}

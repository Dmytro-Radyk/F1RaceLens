import { LocalDatePipe } from '../../shared/local-date.pipe';
import { LanguagePicker } from './language-picker';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router, TitleStrategy } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { LocaleService, currentLocale } from './locale.service';
import { LocalizedTitle } from './localized-title';
import * as translations from './translations';
import { CATALOG_LOADER, type TranslatedLanguage } from './translations';
import { routes } from '../../app.routes';
import { formatScheduleDate } from '../../features/calendar/utils/schedule-format';
import { modelChance } from '../../features/predictions/prediction-table';

const languages: TranslatedLanguage[] = ['en', 'de', 'it', 'es', 'fr'];
const placeholders = (value: string) =>
  [...value.matchAll(/\{\d+\}/g)].map((item) => item[0]).sort();

describe('Interface languages', () => {
  let loader = vi.fn(translations.loadCatalog);
  beforeEach(() => {
    localStorage.clear();
    loader = vi.fn(translations.loadCatalog);
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CATALOG_LOADER, useValue: loader },
        { provide: TitleStrategy, useClass: LocalizedTitle },
      ],
    });
  });
  afterEach(async () => {
    vi.restoreAllMocks();
    await TestBed.inject(LocaleService).setLanguage('uk');
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
  });

  it.each(languages)('has a complete %s catalog with matching placeholders', async (language) => {
    const reference = await translations.loadCatalog('en');
    const catalog = await translations.loadCatalog(language);
    expect(Object.keys(catalog).sort()).toEqual(Object.keys(reference).sort());
    expect(Object.keys(catalog).length).toBeGreaterThan(570);
    for (const [key, value] of Object.entries(catalog)) {
      expect(value.trim(), key).not.toBe('');
      expect(placeholders(value), key).toEqual(placeholders(key));
    }
  });

  it('restores a supported saved language before rendering', async () => {
    localStorage.setItem('f1-race-lens-language', 'de');
    const locale = TestBed.inject(LocaleService);
    await locale.ready;
    expect(locale.language()).toBe('de');
    expect(document.documentElement.lang).toBe('de');
    expect(locale.text('Пілоти')).toBe('Fahrer');
    expect(currentLocale()).toBe('de-DE');
    const picker = TestBed.createComponent(LanguagePicker);
    picker.detectChanges();
    expect((picker.nativeElement as HTMLElement).querySelector('select')?.value).toBe('de');
  });

  it('defaults to Ukrainian and ignores unsupported languages', async () => {
    localStorage.setItem('f1-race-lens-language', 'invalid');
    const locale = TestBed.inject(LocaleService);
    await locale.ready;
    await locale.setLanguage('xx');
    expect(locale.language()).toBe('uk');
    expect(locale.text('Етап {0}', [18])).toBe('Етап 18');
  });

  it('switches when browser storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const locale = TestBed.inject(LocaleService);
    await locale.setLanguage('it');
    expect(locale.language()).toBe('it');
    expect(locale.text('Команди')).toBe('Scuderie');
  });

  it('retains the working language when a catalog fails and allows retry', async () => {
    const locale = TestBed.inject(LocaleService);
    await locale.setLanguage('en');
    const load = loader.mockRejectedValueOnce(new Error('offline'));
    await locale.setLanguage('fr');
    expect(locale.language()).toBe('en');
    expect(locale.failed()).toBe(true);
    load.mockImplementation(translations.loadCatalog);
    await locale.setLanguage('fr');
    expect(locale.language()).toBe('fr');
    expect(locale.failed()).toBe(false);
  });

  it('keeps only the latest requested language during rapid changes', async () => {
    const locale = TestBed.inject(LocaleService);
    const catalog = await translations.loadCatalog('de');
    let finish: (value: translations.Catalog) => void = () => {};
    loader.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const old = locale.setLanguage('de');
    await locale.setLanguage('uk');
    finish(catalog);
    await old;
    expect(locale.language()).toBe('uk');
    expect(locale.loading()).toBe(false);
    expect(localStorage.getItem('f1-race-lens-language')).toBe('uk');
  });

  it('translates parameterized chart labels and formats dates and chance bounds', async () => {
    const locale = TestBed.inject(LocaleService);
    await locale.setLanguage('en');
    expect(locale.text('Етап {0} · {1}', [18, 'United States Grand Prix'])).toBe(
      'Round 18 · United States Grand Prix',
    );
    expect(locale.text('Старт: 3 · Схід')).toBe('Grid: 3 · Retired');
    expect(locale.text('Точка старту. Етап 18 · Austin. Старт: 3 · Схід')).toBe(
      'Grid point. Round 18 · Austin. Grid: 3 · Retired',
    );
    expect(locale.text('Max Verstappen')).toBe('Max Verstappen');
    expect(formatScheduleDate({ date: '2026-10-25', startsAt: null })).toBe('25 October');
    expect(modelChance(0, false)).toBe('<0.1%');
    const date = new LocalDatePipe();
    expect(date.transform('1950-05-13')).toBe('13 May 1950');
    await locale.setLanguage('de');
    expect(formatScheduleDate({ date: '2026-10-25', startsAt: null })).toBe('25. Oktober');
    expect(modelChance(100, false)).toBe('>99,9%');
    expect(date.transform('1950-05-13')).toBe('13. Mai 1950');
  });

  it('changes the live page and title without changing the year, filter, URL or refetching data', async () => {
    const harness = await RouterTestingHarness.create('/calendar?season=2025');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/seasons/2025/calendar').flush({
      season: 2025,
      updatedAt: '2026-10-19T12:00:00Z',
      isStale: false,
      source: 'Jolpica',
      races: [
        {
          season: 2025,
          round: 1,
          name: 'Australian Grand Prix',
          date: '2025-03-16',
          startsAt: null,
          circuit: {
            id: 'albert_park',
            name: 'Albert Park',
            locality: 'Melbourne',
            country: 'Australia',
          },
          sessions: [],
        },
      ],
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Етапи сезону');
    });
    const page = harness.routeNativeElement!;
    const past = [...page.querySelectorAll<HTMLButtonElement>('.calendar-filters button')].find(
      (button) => button.textContent?.trim() === 'Минулі',
    )!;
    past.click();
    harness.detectChanges();
    const picker = page.querySelector<HTMLSelectElement>('app-language-picker select')!;
    expect(picker.options.length).toBe(6);
    picker.value = 'fr';
    picker.dispatchEvent(new Event('change'));
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page.textContent).toContain('Manches de saison');
    });
    expect(TestBed.inject(Router).url).toBe('/calendar?season=2025');
    expect(page.querySelector<HTMLSelectElement>('.season-picker select')!.value).toBe('2025');
    expect(past.getAttribute('aria-pressed')).toBe('true');
    expect(document.title).toBe('Calendrier de saison · F1 Race Lens');
    expect(document.documentElement.lang).toBe('fr');
    expect(localStorage.getItem('f1-race-lens-language')).toBe('fr');
    http.expectNone('/api/seasons/2025/calendar');
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import axe from 'axe-core';
import { routes } from '../../app.routes';
import { sortCareer, matchesCareer, CareerEntry, CareerData } from './catalog.service';
import { HomeNews } from '../news/components/home-news';
import { NewsImage } from '../news/components/news-image';

const driver: CareerEntry = {
  id: 'michael-schumacher',
  name: 'Michael Schumacher',
  fullName: 'Michael Schumacher',
  country: 'DE',
  born: '1969-01-03',
  died: null,
  birthplace: 'Hürth',
  titles: 7,
  wins: 91,
  entries: 308,
  starts: 306,
  podiums: 155,
  poles: 68,
  fastestLaps: 77,
  points: 1566,
  seasons: [1991, 1994, 1995, 2000],
  titleYears: [1994, 1995, 2000],
};
const other: CareerEntry = {
  ...driver,
  id: 'lewis-hamilton',
  name: 'Lewis Hamilton',
  fullName: 'Lewis Hamilton',
  wins: 105,
};
const catalog: CareerData = {
  drivers: [driver, other],
  constructors: [
    { ...driver, id: 'ferrari', name: 'Ferrari', fullName: 'Scuderia Ferrari', titles: 16 },
  ],
  lineage: [],
  source: 'F1DB',
  version: 'v2026.17.0',
  sourceUrl: 'https://github.com/f1db/f1db',
  licenseUrl: 'https://creativecommons.org/licenses/by/4.0/',
  throughSeason: 2026,
  throughRound: 17,
};
const feed = {
  source: 'BBC Sport',
  sourceUrl: 'https://www.bbc.com/sport/formula1',
  language: 'en',
  updatedAt: '2026-10-21T10:00:00Z',
  isStale: false,
  articles: [
    {
      title: 'First headline',
      url: 'https://www.bbc.co.uk/sport/formula1/articles/one',
      publishedAt: '2026-10-21T09:00:00Z',
    },
    {
      title: 'Second headline',
      url: 'https://www.bbc.co.uk/sport/formula1/articles/two',
      publishedAt: '2026-10-20T09:00:00Z',
    },
  ],
};

describe('F1 explorer', () => {
  it('opens the history timeline with correct anchor URLs under the application base path', async () => {
    const harness = await RouterTestingHarness.create('/history');
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelectorAll('.history-chapter')).toHaveLength(8);
    expect(harness.routeNativeElement!.querySelector('a[href="/history#1950"]')).not.toBeNull();
    expect(
      (
        await axe.run(harness.routeNativeElement!.querySelector('.catalog-page')!, {
          rules: { 'color-contrast': { enabled: false } },
        })
      ).violations,
    ).toEqual([]);
  });
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
  });

  it('sorts by titles then wins, supports alphabetical order and accent-insensitive aliases', () => {
    expect(sortCareer([driver, other], '', 'titles', 'en')[0].id).toBe(other.id);
    expect(sortCareer([driver, other], '', 'name', 'en').map((row) => row.name)).toEqual([
      'Lewis Hamilton',
      'Michael Schumacher',
    ]);
    expect(matchesCareer(driver, 'Міхаель Шумахер')).toBe(true);
    expect(
      matchesCareer({ ...driver, name: 'José Example', fullName: 'José Example' }, 'Jose'),
    ).toBe(true);
    expect(matchesCareer(driver, 'Norris')).toBe(false);
  });

  it('opens all-time drivers by default and supports filtering without refetching', async () => {
    const harness = await RouterTestingHarness.create('/drivers');
    const http = TestBed.inject(HttpTestingController);
    await vi.waitFor(() => http.expectOne('/api/catalog').flush(catalog));
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Michael Schumacher');
    });
    const page = harness.routeNativeElement!;
    expect(page.querySelectorAll('.career-card')).toHaveLength(2);
    const filter = page.querySelector<HTMLInputElement>('input')!;
    filter.value = 'Міхаель';
    filter.dispatchEvent(new Event('input'));
    harness.detectChanges();
    expect(page.querySelectorAll('.career-card')).toHaveLength(1);
    expect(page.querySelector('h3 a')?.getAttribute('href')).toBe(
      '/drivers/career/michael-schumacher',
    );
    const report = await axe.run(page.querySelector('.catalog-page')!, {
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(report.violations).toEqual([]);
  });

  it('shows career facts, title years and season links without using F1DB IDs in Jolpica routes', async () => {
    const harness = await RouterTestingHarness.create('/drivers/career/michael-schumacher');
    await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/catalog').flush(catalog),
    );
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Чемпіонські сезони');
    });
    expect(harness.routeNativeElement?.textContent).toContain('1994, 1995, 2000');
    expect(harness.routeNativeElement?.querySelector('.season-links a')?.getAttribute('href')).toBe(
      '/drivers?season=1991&view=season',
    );
    expect(harness.routeNativeElement?.textContent).toContain('3 січня 1969');
  });

  it('shows a clear error and allows directory retry', async () => {
    const harness = await RouterTestingHarness.create('/teams');
    const http = TestBed.inject(HttpTestingController);
    await vi.waitFor(() =>
      http.expectOne('/api/catalog').flush({}, { status: 503, statusText: 'Unavailable' }),
    );
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Довідник тимчасово недоступний');
    });
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('.catalog-page button')!.click();
    harness.detectChanges();
    await vi.waitFor(() => http.expectOne('/api/catalog').flush(catalog));
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Ferrari');
    });
  });

  it('shows one headline and supports buttons, swipe and cancellation', async () => {
    const fixture = TestBed.createComponent(HomeNews);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/news').flush(feed);
    await fixture.whenStable();
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelectorAll('h3')).toHaveLength(1);
    expect(page.textContent).toContain('First headline');
    page.querySelector<HTMLButtonElement>('[aria-label="Наступна новина"]')!.click();
    fixture.detectChanges();
    expect(page.querySelector('h3')?.textContent).toContain('Second headline');
    const send = (type: string, x: number) => {
      const event = new Event(type, { bubbles: true });
      Object.defineProperties(event, {
        clientX: { value: x },
        clientY: { value: 0 },
        pointerId: { value: 1 },
      });
      page.querySelector('.story')!.dispatchEvent(event);
    };
    send('pointerdown', 200);
    send('pointerup', 40);
    fixture.detectChanges();
    expect(page.querySelector('h3')?.textContent).toContain('First headline');
    send('pointerdown', 200);
    send('pointercancel', 100);
    send('pointerup', 40);
    fixture.detectChanges();
    expect(page.querySelector('h3')?.textContent).toContain('First headline');
    expect(
      (await axe.run(page, { rules: { 'color-contrast': { enabled: false } } })).violations,
    ).toEqual([]);
    fixture.destroy();
  });

  it('falls back to an article-specific banner when its thumbnail fails', () => {
    const fixture = TestBed.createComponent(NewsImage);
    fixture.componentRef.setInput('title', 'Specific story');
    fixture.componentRef.setInput('url', 'https://ichef.bbci.co.uk/example.jpg');
    fixture.detectChanges();
    fixture.nativeElement.querySelector('img').dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Specific story');
    fixture.destroy();
  });

  it('searches career records and pages while independently handling live source failure', async () => {
    const harness = await RouterTestingHarness.create('/search?q=Шумахер');
    const http = TestBed.inject(HttpTestingController);
    await vi.waitFor(() => http.expectOne('/api/catalog').flush(catalog));
    http.expectOne('/api/news').flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/seasons/current/calendar').flush({
      season: 2026,
      updatedAt: feed.updatedAt,
      isStale: false,
      source: 'Jolpica',
      races: [],
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Michael Schumacher');
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Новини тимчасово недоступні');
    });
    expect(
      harness.routeNativeElement?.querySelector('a[href="/drivers/career/michael-schumacher"]'),
    ).not.toBeNull();
    const input = harness.routeNativeElement!.querySelector<HTMLInputElement>('input')!;
    input.value = 'Halo';
    input.dispatchEvent(new Event('input'));
    harness
      .routeNativeElement!.querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() =>
      http.expectOne('/api/seasons/current/calendar').flush({
        season: 2026,
        updatedAt: feed.updatedAt,
        isStale: false,
        source: 'Jolpica',
        races: [],
      }),
    );
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Безпека як постійний розвиток');
    });
  });
});

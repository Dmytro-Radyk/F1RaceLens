import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import axe from 'axe-core';
import { F1News } from './f1-news';

describe('F1 news', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [F1News],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows original headlines as text, attribution, dates and safe external links', async () => {
    const fixture = TestBed.createComponent(F1News);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/news')
      .flush({
        source: 'BBC Sport',
        sourceUrl: 'https://www.bbc.com/sport/formula1',
        language: 'en',
        updatedAt: '2026-10-21T12:00:00Z',
        isStale: true,
        articles: [
          {
            title: '<img src=x onerror=alert(1)> F1 news',
            url: 'https://www.bbc.co.uk/sport/formula1/articles/example',
            publishedAt: '2026-10-20T12:00:00Z',
          },
        ],
      });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Показуємо збережені новини');
    expect(element.querySelector('h3 a')?.textContent).toContain('<img');
    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('h3 a')?.getAttribute('rel')).toContain('noopener');
    expect(element.querySelector('time')?.getAttribute('datetime')).toBe('2026-10-20T12:00:00Z');
    const report = await axe.run(element, { rules: { 'color-contrast': { enabled: false } } });
    expect(report.violations).toEqual([]);
    fixture.destroy();
  });

  it('supports unavailable feed and retry', async () => {
    const fixture = TestBed.createComponent(F1News);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/news').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Новини тимчасово недоступні');
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();
    http.expectOne('/api/news').flush({
      source: 'BBC Sport',
      sourceUrl: 'https://www.bbc.com/sport/formula1',
      language: 'en',
      updatedAt: '2026-10-21T12:00:00Z',
      isStale: false,
      articles: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Новин поки немає');
    fixture.destroy();
  });
});

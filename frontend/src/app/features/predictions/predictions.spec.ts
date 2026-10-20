import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { modelChance } from './prediction-table';
import type { PredictionResponse, ForecastRow } from './predictions.types';

const row: ForecastRow = {
  id: 'test',
  name: 'Test Driver',
  currentPosition: 2,
  currentPoints: 100,
  active: true,
  expectedPoints: 160.25,
  pointsLow: 120,
  pointsHigh: 190,
  averagePosition: 2.4,
  positionLow: 1,
  positionHigh: 5,
  titleChance: 30,
  top3Chance: 65,
};
const prediction: PredictionResponse = {
  season: 2026,
  updatedAt: '2026-10-19T07:00:00Z',
  isStale: false,
  source: 'Jolpica',
  status: 'ready',
  message: null,
  throughRound: 17,
  remainingRaces: 6,
  remainingSprints: 2,
  simulations: 4000,
  modelVersion: 'classification-bootstrap-v1',
  drivers: [row, { ...row, id: 'reserve', name: 'Reserve', active: false, titleChance: 0 }],
  teams: [{ ...row, id: 'team', name: 'Team' }],
};

describe('Prediction probabilities', () => {
  it('does not present finite-sample 0 and 100 as certainty', () => {
    expect(modelChance(0, false)).toBe('<0,1%');
    expect(modelChance(100, false)).toBe('>99,9%');
    expect(modelChance(0, true)).toBe('0%');
    expect(modelChance(100, true)).toBe('100%');
  });
});
describe('Prediction page', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('shows a failed update and the last success without triggering an update or hiding forecasts', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush(prediction);
    await settle(h, 'Прогнозований залік команд');
    [...h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button')]
      .find((x) => x.textContent?.trim() === 'Оновлення даних')!
      .click();
    await vi.waitFor(() => {
      h.detectChanges();
      const request = http.expectOne('/api/predictions/update-status');
      expect(request.request.method).toBe('GET');
      request.flush({ state: 'failed', stage: 'ml', lastSuccessfulAt: '2026-10-19T12:00:00Z' });
    });
    await settle(h, 'Помилка оновлення');
    expect(h.routeNativeElement!.textContent).toContain('Раніше опубліковані прогнози збережено');
    expect(h.routeNativeElement!.textContent).toContain('Останнє успішне оновлення');
    expect(h.routeNativeElement!.querySelectorAll('app-prediction-table')).toHaveLength(2);
    http.expectNone((request) => request.method === 'POST');
  });
  it('loads ML directly from the URL and keeps it independent from the script', async () => {
    const h = await RouterTestingHarness.create('/predictions?engine=ml');
    http
      .expectOne('/api/predictions/ml')
      .flush({ ...prediction, source: 'Jolpica · LightGBM', modelVersion: 'lightgbm-race-v4' });
    await settle(h, 'Прогнозований залік команд');
    http.expectNone('/api/predictions/current');
    expect(h.routeNativeElement!.textContent).toContain('Збережений розрахунок LightGBM v4');
    expect(h.routeNativeElement!.textContent).not.toContain('Що враховує статистичний скрипт');
    const button = [
      ...h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.forecast-views button'),
    ].find((x) => x.textContent?.trim() === 'Статистичний скрипт')!;
    button.click();
    await vi.waitFor(() => {
      h.detectChanges();
      http.expectOne('/api/predictions/current').flush(prediction);
    });
    await settle(h, 'Що враховує статистичний скрипт');
  });
  it('shows pending journal rows without inventing accuracy numbers', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush(prediction);
    await settle(h, 'Прогнозований залік команд');
    [...h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button')]
      .find((x) => x.textContent?.trim() === 'Перевірка точності')!
      .click();
    await vi.waitFor(() => {
      h.detectChanges();
      http.expectOne('/api/predictions/accuracy').flush({
        rows: [
          {
            engine: 'ml',
            version: 'lightgbm-race-v4',
            round: 18,
            throughRound: 17,
            name: 'Future GP',
            status: 'pending',
            rankMae: null,
            winnerBrier: null,
            winnerHit: null,
          },
        ],
      });
    });
    await settle(h, 'Очікуємо гонку');
    const cells = h.routeNativeElement!.querySelectorAll('app-forecast-accuracy tbody td');
    expect(cells[3].textContent?.trim()).toBe('—');
    expect(cells[4].textContent?.trim()).toBe('—');
    expect(cells[5].textContent?.trim()).toBe('—');
  });
  async function settle(h: RouterTestingHarness, text: string) {
    await vi.waitFor(() => {
      h.detectChanges();
      expect(h.routeNativeElement?.textContent).toContain(text);
    });
  }
  it('shows current-season tables, intervals, roster caveats and calibration limitations', async () => {
    const h = await RouterTestingHarness.create('/predictions?season=1950');
    http.expectOne('/api/predictions/current').flush(prediction);
    await settle(h, 'Прогнозований залік команд');
    const page = h.routeNativeElement!;
    expect(page.querySelectorAll('app-prediction-table')).toHaveLength(2);
    expect(page.querySelector('a[href="/drivers/test?season=2026"]')).not.toBeNull();
    expect(page.querySelector('a[href="/teams/team?season=2026"]')).not.toBeNull();
    expect(page.textContent).toContain('Без майбутніх нарахувань');
    expect(page.textContent).toContain('Відсотки ще не відкалібровані');
    expect(page.textContent).toContain('120–190');
    expect(page.querySelector('app-prediction-table')?.textContent).toContain('160');
    expect(page.querySelector('app-prediction-table')?.textContent).not.toContain('160,3');
    expect(page.querySelector('app-prediction-table')?.textContent).not.toContain('Зараз');
    expect(page.querySelector('.primary-nav .is-active')?.getAttribute('href')).toBe(
      '/predictions',
    );
    http.expectNone((request) => request.url.includes('1950'));
  });
  it('shows only future race forecasts and switches between their rounds', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    const raceRow = {
      id: 'test',
      name: 'Future Driver',
      expectedPoints: 18.6,
      averagePosition: 2.3,
      winChance: 20,
      podiumChance: 50,
      nonClassificationChance: 10,
    };
    http.expectOne('/api/predictions/current').flush({
      ...prediction,
      races: [
        { round: 18, name: 'Future GP', date: '2026-10-25', drivers: [raceRow] },
        { round: 23, name: 'Final GP', date: '2026-12-06', drivers: [raceRow] },
      ],
    });
    await settle(h, 'Прогнозований залік команд');
    Array.from(h.routeNativeElement!.querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === 'Майбутні Гран-прі')!
      .click();
    await settle(h, 'Future GP');
    expect(h.routeNativeElement!.textContent).toContain('Future Driver');
    expect(h.routeNativeElement!.textContent).not.toContain('Прогнозований залік команд');
    const select = h.routeNativeElement!.querySelector<HTMLSelectElement>('#forecast-race')!;
    select.value = '23';
    select.dispatchEvent(new Event('change'));
    await settle(h, 'Final GP');
    expect(h.routeNativeElement!.querySelector('tbody')?.textContent).toContain('19');
  });
  it('marks stale predictions independently of the simulation output', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush({ ...prediction, isStale: true });
    await settle(h, 'Показуємо збережений прогноз');
  });
  it('does not fabricate probabilities while inputs are synchronizing', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush({
      ...prediction,
      status: 'synchronizing',
      message: 'Неузгоджені результати',
      drivers: [],
      teams: [],
      simulations: 0,
    });
    await settle(h, 'Неузгоджені результати');
    expect(h.routeNativeElement!.querySelector('app-prediction-table')).toBeNull();
  });
  it('retries failed source requests', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http
      .expectOne('/api/predictions/current')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle(h, 'Прогноз тимчасово недоступний');
    h.routeNativeElement!.querySelector<HTMLButtonElement>('.feedback-panel button')!.click();
    h.detectChanges();
    await vi.waitFor(() => http.expectOne('/api/predictions/current').flush(prediction));
    await settle(h, 'Прогнозований залік команд');
  });
  it('shows a completed championship without implying future simulation', async () => {
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush({
      ...prediction,
      status: 'completed',
      remainingRaces: 0,
      remainingSprints: 0,
      simulations: 1,
    });
    await settle(h, 'У календарі не залишилося гонок');
    expect(h.routeNativeElement!.textContent).not.toContain('Модельні шанси — не гарантія');
  });
  it('submits an explicit scenario, preserves the selected controls and restores the baseline', async () => {
    const factors = {
      events: [
        {
          round: 18,
          sprint: false,
          circuitId: 'track',
          name: 'GP',
          date: '2026-10-25',
          historicalRaces: 2,
          averageGridChange: 3,
          nonClassificationRate: 0.1,
          rainProbability: null,
          temperature: null,
          wind: null,
          weatherStatus: 'outside-horizon',
          weatherUpdatedAt: null,
        },
      ],
      historicalSeasons: [2024, 2025],
      isScenario: false,
      settings: {
        weather: 'auto',
        developmentTeamId: null,
        developmentPositions: 0,
        replacement: null,
      },
      rosterIds: ['test'],
    };
    const data = { ...prediction, factors };
    const h = await RouterTestingHarness.create('/predictions');
    http.expectOne('/api/predictions/current').flush(data);
    await settle(h, 'Перевірити сценарій');
    const select = (id: string, value: string) => {
      const control = h.routeNativeElement!.querySelector<HTMLSelectElement>(id)!;
      control.value = value;
      control.dispatchEvent(new Event('change'));
      h.detectChanges();
    };
    expect(
      h.routeNativeElement!.querySelector<HTMLSelectElement>('#scenario-development')!.value,
    ).toBe('0');
    select('#scenario-weather', 'wet');
    select('#scenario-team', 'team');
    select('#scenario-development', '2');
    h.routeNativeElement!.querySelector<HTMLInputElement>('.replacement-toggle input')!.click();
    h.detectChanges();
    select('#scenario-outgoing', 'test');
    select('#scenario-incoming', 'reserve');
    h.routeNativeElement!.querySelector<HTMLButtonElement>(
      '.scenario-actions .primary-link',
    )!.click();
    h.detectChanges();
    const settings = {
      weather: 'wet',
      developmentTeamId: 'team',
      developmentPositions: 2,
      replacement: { outgoingId: 'test', incomingId: 'reserve', fromRound: 18 },
    };
    await vi.waitFor(() => {
      const request = http.expectOne('/api/predictions/scenario');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(settings);
      request.flush({ ...data, factors: { ...factors, isScenario: true, settings } });
    });
    await settle(h, 'Результат заданого сценарію');
    expect(h.routeNativeElement!.querySelector<HTMLSelectElement>('#scenario-weather')!.value).toBe(
      'wet',
    );
    expect(
      h.routeNativeElement!.querySelector<HTMLSelectElement>('#scenario-outgoing')!.value,
    ).toBe('test');
    h.routeNativeElement!.querySelector<HTMLButtonElement>('.scenario-notice button')!.click();
    h.detectChanges();
    await vi.waitFor(() => http.expectOne('/api/predictions/current').flush(data));
    await settle(h, 'Чинники майбутніх сесій');
    expect(h.routeNativeElement!.querySelector('.scenario-notice')).toBeNull();
    expect(h.routeNativeElement!.querySelector<HTMLSelectElement>('#scenario-weather')!.value).toBe(
      'auto',
    );
  });
});

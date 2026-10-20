import { TestBed } from '@angular/core/testing';
import { LocaleService } from './locale.service';
import { RaceStatus, raceStatuses } from './race-status';
import { citizenshipCountry } from './regions';
import type { Language } from './translations';
import { resultLabel } from '../../features/compare/comparison';
import type { DriverRace } from '../../features/drivers/models/driver-profile.types';

const examples: readonly [Language, string, string, string, string][] = [
  ['uk', 'Схід', 'Схід: двигун', '+21 коло', 'Італія'],
  ['en', 'Retired', 'Retired: engine', '+21 laps', 'Italy'],
  ['de', 'Ausgeschieden', 'Ausfall: Motor', '+21 Runden', 'Italien'],
  ['it', 'Ritirato', 'Ritiro: motore', '+21 giri', 'Italia'],
  ['es', 'Abandono', 'Abandono: motor', '+21 vueltas', 'Italia'],
  ['fr', 'Abandon', 'Abandon : moteur', '+21 tours', 'Italie'],
];

describe('Localised source results', () => {
  beforeEach(() => localStorage.clear());
  afterEach(async () => {
    await TestBed.inject(LocaleService).setLanguage('uk');
    localStorage.clear();
  });

  it.each(examples)(
    'formats source statuses and citizenship in %s',
    async (language, retired, engine, laps, country) => {
      const locale = TestBed.inject(LocaleService);
      await locale.setLanguage(language);
      const status = TestBed.inject(RaceStatus);
      expect(status.text('Retired')).toBe(retired);
      expect(status.text('Engine')).toBe(engine);
      expect(status.text('+21 Laps')).toBe(laps);
      expect(status.text('+1 Lap')).not.toContain('Laps');
      expect(citizenshipCountry('Italian')).toBe(country);
      expect(status.text(null)).toBe(locale.text('Не вказано'));
      expect(status.text('')).toBe(locale.text('Не вказано'));
      for (const [source, message] of Object.entries(raceStatuses)) {
        const translated = status.text(source);
        expect(translated, source).toBe(locale.text(message));
        if (language !== 'uk') expect(translated, source).not.toMatch(/[А-Яа-яІіЇїЄєҐґ]/);
        if (language === 'uk') expect(translated, source).toMatch(/[А-Яа-яІіЇїЄєҐґ]/);
      }
    },
  );

  it('uses Ukrainian plural forms and preserves unknown source values with a translated label', async () => {
    const locale = TestBed.inject(LocaleService);
    await locale.ready;
    const status = TestBed.inject(RaceStatus);
    expect([1, 2, 5, 11, 21, 22].map((n) => status.text(`+${n} Laps`))).toEqual([
      '+1 коло',
      '+2 кола',
      '+5 кіл',
      '+11 кіл',
      '+21 коло',
      '+22 кола',
    ]);
    for (const value of ['Future failure', 'constructor', 'toString']) {
      expect(status.text(value)).toBe(`Інший статус (дані джерела): ${value}`);
    }
    expect(citizenshipCountry('Unknown nationality')).toBe('Unknown nationality');
    expect(citizenshipCountry('Rhodesian')).toBe('Родезія');
  });

  it('translates each historical result before joining multiple entries', async () => {
    const locale = TestBed.inject(LocaleService);
    const status = TestBed.inject(RaceStatus);
    const races: readonly DriverRace[] = ['Engine', 'Retired'].map((value, index) => ({
      round: 1,
      name: 'Historic Grand Prix',
      date: '1953-07-18',
      circuit: 'Silverstone',
      result: {
        status: value,
        position: index + 1,
        positionText: String(index + 1),
        driverId: 'test',
        driverName: 'Test Driver',
        driverCode: null,
        team: 'Test Team',
        grid: 1,
        laps: 1,
        time: null,
        points: 0,
        q1: null,
        q2: null,
        q3: null,
      },
    }));
    await locale.setLanguage('fr');
    const label = resultLabel(
      races,
      (message, parameters) => locale.text(message, parameters),
      (value) => status.text(value),
    );
    expect(label).toBe(
      'Classement : 1 ; statut : Abandon : moteur / Classement : 2 ; statut : Abandon',
    );
    expect(label).not.toMatch(/Engine|Retired|[А-Яа-яІіЇїЄєҐґ]/);
  });
});

import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

export const historyChapters = [
  {
    year: '1950',
    title: 'Народження чемпіонату світу',
    text: '13 травня 1950 року в Сільверстоуні відбулася перша гонка чемпіонату світу. Першим чемпіоном став Ніно Фаріна на Alfa Romeo. Ранні календарі також включали Indianapolis 500.',
    source:
      'https://www.formula1.com/en/latest/article/why-is-it-called-formula-1-and-12-other-questions-about-the-championships.1GHeel6u4jga6hMpX2eFs1',
  },
  {
    year: '1958',
    title: 'Поява заліку конструкторів',
    text: 'До боротьби пілотів за титул додали окремий залік конструкторів. Його першим переможцем став Vanwall. Саме тому для сезонів 1950–1957 не існує командного чемпіонського заліку.',
    source:
      'https://www.formula1.com/en/latest/article/the-beginners-guide-to-the-f1-constructors-championship.66nTfWSqrUYv3bnbosPkHV',
  },
  {
    year: '1959–1970',
    title: 'Компонування та аеродинаміка',
    text: 'Успіхи Cooper допомогли утвердити розташування двигуна позаду пілота. Згодом антикрила зробили аеродинаміку одним із головних напрямів розвитку гоночних машин.',
    source: 'https://www.mercedes-amg.com/en/formula-1-technical-revolution',
  },
  {
    year: '1970–2000',
    title: 'Технологічне змагання',
    text: 'Команди розвивали аеродинаміку, матеріали шасі та силові установки. Турбодвигуни, електронні системи й зміни технічного регламенту неодноразово змінювали співвідношення сил.',
    source:
      'https://www.formula1.com/en/latest/article/1950-vs-2020-cars-drivers-safety-and-pitstops-how-f1-has-changed-in-70-years.62ITx6LlIY3qgzTctXgz1j',
  },
  {
    year: '1994–2018',
    title: 'Безпека як постійний розвиток',
    text: 'Захист пілота, конструкція машин і безпека трас поступово посилювалися. У 2018 році обов’язковим став Halo — захисна конструкція навколо кокпіта.',
    source:
      'https://www.fia.com/news/legacy-safety-how-fia-responded-tragedy-imola-1994-and-launched-drive-safer-racing',
  },
  {
    year: '2014',
    title: 'Гібридна ера',
    text: 'Формула-1 перейшла на турбовані гібридні силові установки V6. Рекуперація енергії та ефективність стали важливою частиною швидкості й стратегії.',
    source:
      'https://www.formula1.com/en/information/drivers-hall-of-fame-lewis-hamilton.6TbSwzJA2miTUwtnXUHkjk',
  },
  {
    year: '2022–2025',
    title: 'Сучасна аеродинаміка та стратегія',
    text: 'Новий регламент 2022 року посилив роль днища у створенні притискної сили. Сучасна гонка поєднує роботу пілота, темп машини, шини, стратегію та рішення команди.',
    source:
      'https://www.fia.com/news/f1s-new-era-everything-you-need-know-about-how-fia-making-formula-1-more-competitive-more',
  },
  {
    year: '2026',
    title: 'Новий технічний регламент',
    text: 'Оновлено правила шасі та силових установок. Активна аеродинаміка й більша роль електричної складової змінюють роботу машин та управління енергією.',
    source: 'https://corp.formula1.com/f1-2026-regulations-terminology-update/',
  },
];

@Component({
  selector: 'app-f1-history',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './f1-history.html',
  styleUrl: '../catalog/catalog.css',
})
export class F1History {
  protected readonly chapters = historyChapters;
  protected sourceName(url: string): string {
    return url.includes('fia.com')
      ? 'FIA'
      : url.includes('mercedes-amg.com')
        ? 'Mercedes-AMG'
        : 'Formula 1';
  }
}

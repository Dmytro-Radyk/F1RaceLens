import { inject, Service } from '@angular/core';
import { currentLocale, LocaleService } from './locale.service';

// Jolpica status values are source data. Translate only their presentation.
export const raceStatuses: Readonly<Record<string, string>> = {
  Finished: 'Фінішував',
  Retired: 'Схід',
  Engine: 'Схід: двигун',
  Accident: 'Схід: аварія',
  Collision: 'Схід: зіткнення',
  Gearbox: 'Схід: коробка передач',
  'Spun off': 'Схід: виліт із траси',
  Suspension: 'Підвіска',
  Lapped: 'Відставання на кола',
  Transmission: 'Трансмісія',
  Electrical: 'Електрика',
  Brakes: 'Гальма',
  Withdrew: 'Знявся з участі',
  Clutch: 'Зчеплення',
  'Not classified': 'Не класифіковано',
  'Fuel system': 'Паливна система',
  Disqualified: 'Дискваліфіковано',
  Turbo: 'Турбонаддув',
  Hydraulics: 'Гідравліка',
  Overheating: 'Перегрів',
  Ignition: 'Запалювання',
  'Oil leak': 'Витік мастила',
  Throttle: 'Дросельна заслінка',
  'Out of fuel': 'Закінчилося пальне',
  Halfshaft: 'Піввісь',
  Wheel: 'Колесо',
  'Oil pressure': 'Тиск мастила',
  'Fuel pump': 'Паливний насос',
  Differential: 'Диференціал',
  Tyre: 'Шина',
  Handling: 'Керованість',
  'Fuel leak': 'Витік пального',
  'Collision damage': 'Пошкодження після зіткнення',
  Steering: 'Кермове керування',
  Radiator: 'Радіатор',
  'Power Unit': 'Силова установка',
  Puncture: 'Прокол шини',
  'Wheel bearing': 'Підшипник колеса',
  Injection: 'Впорскування пального',
  'Fuel pressure': 'Тиск пального',
  'Water leak': 'Витік охолоджувальної рідини',
  Alternator: 'Генератор',
  Physical: 'Фізичний стан',
  Exhaust: 'Вихлопна система',
  Chassis: 'Шасі',
  Mechanical: 'Механічна несправність',
  Magneto: 'Магнето',
  Driveshaft: 'Приводний вал',
  Axle: 'Вісь',
  'Heat shield fire': 'Займання теплозахисту',
  Battery: 'Акумулятор',
  'Did not start': 'Не стартував',
  Distributor: 'Розподільник запалювання',
  'Oil pump': 'Масляний насос',
  'Power loss': 'Втрата потужності',
  Injury: 'Травма',
  Injured: 'Травма',
  'Oil pipe': 'Мастилопровід',
  'Broken wing': 'Зламане антикрило',
  Electronics: 'Електроніка',
  'Driver unwell': 'Погане самопочуття пілота',
  Vibrations: 'Вібрації',
  'Rear wing': 'Заднє антикрило',
  'Wheel nut': 'Гайка колеса',
  Excluded: 'Виключено',
  'Water pressure': 'Тиск охолоджувальної рідини',
  'Water pump': 'Насос охолоджувальної рідини',
  ERS: 'Система рекуперації енергії',
  'Front wing': 'Переднє антикрило',
  Supercharger: 'Механічний нагнітач',
  Fuel: 'Пальне',
  Pneumatics: 'Пневматика',
  Technical: 'Технічна несправність',
  'Fatal accident': 'Аварія зі смертельним наслідком',
  Fire: 'Пожежа',
  'Fuel pipe': 'Паливопровід',
  'Spark plugs': 'Свічки запалювання',
  Undertray: 'Днище',
  'Water pipe': 'Трубопровід охолодження',
  'Wheel rim': 'Колісний диск',
  Damage: 'Пошкодження',
  Drivetrain: 'Привод',
  'Fuel rig': 'Заправне обладнання',
  Illness: 'Хвороба',
  'Oil line': 'Мастилопровід',
  Safety: 'Безпека',
  'Safety concerns': 'Міркування безпеки',
  Stalled: 'Двигун заглух',
  'Track rod': 'Кермова тяга',
  'Brake duct': 'Повітропровід гальм',
  'CV joint': 'Шарнір рівних кутових швидкостей',
  'Cooling system': 'Система охолодження',
  Crankshaft: 'Колінчастий вал',
  Debris: 'Уламки',
  'Driver Seat': 'Сидіння пілота',
  'Engine fire': 'Займання двигуна',
  'Engine misfire': 'Перебої запалювання',
  'Eye injury': 'Травма ока',
  'Launch control': 'Система контролю старту',
  'Not restarted': 'Не вийшов на повторний старт',
  Refuelling: 'Дозаправлення',
  'Safety belt': 'Ремінь безпеки',
  Seat: 'Сидіння',
  'Tyre puncture': 'Прокол шини',
  Underweight: 'Недостатня маса боліда',
};

@Service()
export class RaceStatus {
  private readonly locale = inject(LocaleService);

  text(value: string | null | undefined): string {
    if (!value?.trim()) return this.locale.text('Не вказано');
    const status = value.trim();
    const laps = /^\+(\d+) Laps?$/.exec(status);
    if (laps) {
      const count = Number(laps[1]);
      const plural = new Intl.PluralRules(currentLocale()).select(count);
      const message = plural === 'one' ? '+{0} коло' : plural === 'few' ? '+{0} кола' : '+{0} кіл';
      return this.locale.text(message, [new Intl.NumberFormat(currentLocale()).format(count)]);
    }
    return Object.hasOwn(raceStatuses, status)
      ? this.locale.text(raceStatuses[status])
      : this.locale.text('Інший статус (дані джерела): {0}', [status]);
  }
}

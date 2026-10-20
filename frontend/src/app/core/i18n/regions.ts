import { currentLocale } from './locale.service';

// Source nationalities are adjectives; use country names with a matching field label.
const citizenships: Readonly<Record<string, readonly string[]>> = {
  American: ['US'],
  Argentine: ['AR'],
  Argentinian: ['AR'],
  Australian: ['AU'],
  Austrian: ['AT'],
  Belgian: ['BE'],
  Brazilian: ['BR'],
  British: ['GB'],
  Canadian: ['CA'],
  Chilean: ['CL'],
  Chinese: ['CN'],
  Colombian: ['CO'],
  Czech: ['CZ'],
  Danish: ['DK'],
  Dutch: ['NL'],
  Finnish: ['FI'],
  French: ['FR'],
  German: ['DE'],
  Hungarian: ['HU'],
  Indian: ['IN'],
  Indonesian: ['ID'],
  Irish: ['IE'],
  Italian: ['IT'],
  Japanese: ['JP'],
  Liechtensteiner: ['LI'],
  Malaysian: ['MY'],
  Mexican: ['MX'],
  Monegasque: ['MC'],
  'New Zealander': ['NZ'],
  Polish: ['PL'],
  Portuguese: ['PT'],
  Russian: ['RU'],
  'South African': ['ZA'],
  Spanish: ['ES'],
  Swedish: ['SE'],
  Swiss: ['CH'],
  Thai: ['TH'],
  Uruguayan: ['UY'],
  Venezuelan: ['VE'],
  'American-Italian': ['US', 'IT'],
  'Argentine-Italian': ['AR', 'IT'],
};

export function citizenshipCountry(value: string | null): string {
  if (!value) return 'Не вказано';
  if (value === 'East German') return 'НДР';
  if (value === 'Rhodesian') return 'Родезія';
  const codes = Object.hasOwn(citizenships, value) ? citizenships[value] : undefined;
  if (!codes) return value;
  const names = new Intl.DisplayNames([currentLocale()], { type: 'region' });
  return codes.map((code) => names.of(code) ?? code).join(' / ');
}

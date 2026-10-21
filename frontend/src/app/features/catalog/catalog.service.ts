import { httpResource } from '@angular/common/http';
import { Service, signal } from '@angular/core';
import aliasData from './search-aliases.json';

export interface CareerEntry {
  readonly id: string;
  readonly name: string;
  readonly fullName: string;
  readonly country: string | null;
  readonly born: string | null;
  readonly died: string | null;
  readonly birthplace: string | null;
  readonly titles: number;
  readonly wins: number;
  readonly entries: number;
  readonly starts: number;
  readonly podiums: number;
  readonly poles: number;
  readonly fastestLaps: number;
  readonly points: number;
  readonly seasons: readonly number[];
  readonly titleYears: readonly number[];
}
export interface CareerData {
  readonly drivers: readonly CareerEntry[];
  readonly constructors: readonly CareerEntry[];
  readonly lineage: readonly {
    parentConstructorId: string;
    constructorId: string;
    yearFrom: number;
    yearTo: number | null;
  }[];
  readonly source: string;
  readonly sourceUrl: string;
  readonly version: string;
  readonly licenseUrl: string;
  readonly throughSeason: number;
  readonly throughRound: number;
}

export function searchText(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/\p{M}/gu, '')
    .toLocaleLowerCase()
    .replace(/[-_]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

// Search aliases are data, not translated interface messages.
const aliases: Readonly<Record<string, string>> = aliasData;

export function matchesCareer(entry: CareerEntry, query: string): boolean {
  const haystack = searchText(
    `${entry.name} ${entry.fullName} ${entry.id} ${aliases[entry.id] ?? ''}`,
  );
  return searchText(query)
    .split(' ')
    .filter(Boolean)
    .every((word) => haystack.includes(word));
}

export function sortCareer(
  entries: readonly CareerEntry[],
  query: string,
  sort: string,
  locale: string,
): CareerEntry[] {
  const collator = new Intl.Collator(locale, { sensitivity: 'base', numeric: true });
  return entries
    .filter((entry) => matchesCareer(entry, query))
    .sort((a, b) => {
      if (sort === 'titles')
        return b.titles - a.titles || b.wins - a.wins || collator.compare(a.name, b.name);
      if (sort === 'wins')
        return b.wins - a.wins || b.titles - a.titles || collator.compare(a.name, b.name);
      return collator.compare(a.name, b.name);
    });
}

@Service()
export class CareerCatalog {
  private readonly enabled = signal(false);
  readonly data = httpResource<CareerData>(() => (this.enabled() ? '/api/catalog' : undefined));
  load(): void {
    this.enabled.set(true);
  }
}

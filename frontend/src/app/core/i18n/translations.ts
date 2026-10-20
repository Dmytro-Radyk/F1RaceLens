import { InjectionToken } from '@angular/core';

export type Language = 'uk' | 'en' | 'de' | 'it' | 'es' | 'fr';
export type TranslatedLanguage = Exclude<Language, 'uk'>;
export type Catalog = Readonly<Record<string, string>>;
const loaders: Record<TranslatedLanguage, () => Promise<Catalog>> = {
  en: () => import('./catalogs/en').then((module) => module.en),
  de: () => import('./catalogs/de').then((module) => module.de),
  it: () => import('./catalogs/it').then((module) => module.it),
  es: () => import('./catalogs/es').then((module) => module.es),
  fr: () => import('./catalogs/fr').then((module) => module.fr),
};
export function loadCatalog(language: TranslatedLanguage): Promise<Catalog> {
  return loaders[language]();
}

export const CATALOG_LOADER = new InjectionToken<
  (language: TranslatedLanguage) => Promise<Catalog>
>('Language catalog loader', { providedIn: 'root', factory: () => loadCatalog });

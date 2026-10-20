import { DOCUMENT } from '@angular/common';
import { computed, inject, Service, signal } from '@angular/core';
import { CATALOG_LOADER, type Catalog, type Language } from './translations';

export const languages = [
  { id: 'uk', name: 'Українська', locale: 'uk-UA' },
  { id: 'en', name: 'English', locale: 'en-GB' },
  { id: 'de', name: 'Deutsch', locale: 'de-DE' },
  { id: 'it', name: 'Italiano', locale: 'it-IT' },
  { id: 'es', name: 'Español', locale: 'es-ES' },
  { id: 'fr', name: 'Français', locale: 'fr-FR' },
] as const;
const selected = signal<Language>('uk');
export function currentLocale(): string {
  return languages.find((item) => item.id === selected())!.locale;
}
export function isLanguage(value: string | null): value is Language {
  return languages.some((item) => item.id === value);
}
const normalize = (value: string) => value.replace(/\s+/g, ' ').trim();
const buildPatterns = (catalog: Catalog) =>
  Object.keys(catalog)
    .filter((key) => /\{\d+\}/.test(key))
    .sort((a, b) => b.replace(/\{\d+\}/g, '').length - a.replace(/\{\d+\}/g, '').length)
    .map((key) => ({
      key,
      pattern: new RegExp(
        '^' +
          key
            .split(/(\{\d+\})/)
            .map((part) =>
              /^\{\d+\}$/.test(part) ? '(.*?)' : part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
            )
            .join('') +
          '$',
      ),
    }));

@Service()
export class LocaleService {
  private readonly document = inject(DOCUMENT);
  private readonly loadCatalog = inject(CATALOG_LOADER);
  private readonly storageKey = 'f1-race-lens-language';
  readonly language = selected.asReadonly();
  readonly languages = languages;
  private readonly messages = signal<Catalog>({});
  private readonly patterns = computed(() => buildPatterns(this.messages()));
  private request = 0;
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly ready: Promise<void>;

  constructor() {
    let stored: string | null = null;
    try {
      stored = this.document.defaultView?.localStorage.getItem(this.storageKey) ?? null;
    } catch {
      /* Language switching also works without browser storage. */
    }
    this.ready = this.changeLanguage(isLanguage(stored) ? stored : 'uk', false);
  }

  async setLanguage(value: string): Promise<void> {
    if (isLanguage(value)) await this.changeLanguage(value, true);
  }

  private async changeLanguage(value: Language, persist: boolean): Promise<void> {
    const request = ++this.request;
    this.loading.set(true);
    this.failed.set(false);
    try {
      const catalog = value === 'uk' ? {} : await this.loadCatalog(value);
      if (request !== this.request) return;
      this.messages.set(catalog);
      this.apply(value);
      if (persist) {
        try {
          this.document.defaultView?.localStorage.setItem(this.storageKey, value);
        } catch {
          /* The in-memory preference remains usable. */
        }
      }
    } catch {
      if (request === this.request) this.failed.set(true);
    } finally {
      if (request === this.request) this.loading.set(false);
    }
  }

  text(value: unknown, parameters: readonly unknown[] = []): string {
    if (value === null || value === undefined) return '';
    const source = String(value);
    const language = this.language();
    const key = normalize(source);
    const catalog = language === 'uk' ? undefined : this.messages();
    const translated = catalog && Object.hasOwn(catalog, key) ? catalog[key] : undefined;
    let result = translated ?? source;
    if (catalog && translated === undefined && /[А-Яа-яІіЇїЄєҐґ]/.test(key)) {
      // Strings assembled by existing chart/status helpers use the same parameterized catalog.
      const match = this.patterns()
        .map((item) => ({ item, groups: key.match(item.pattern) }))
        .find((item) => item.groups !== null);
      if (match?.groups) {
        const groups = match.groups;
        result = catalog[match.item.key].replace(/\{(\d+)\}/g, (_, index: string) =>
          this.text(groups[Number(index) + 1]),
        );
      } else if (key.includes(' · ') || key.includes('. ')) {
        result = key
          .split(/( · |\. )/)
          .map((part) => (/^( · |\. )$/.test(part) ? part : this.text(part)))
          .join('');
      } else if (key.endsWith(' · F1 Race Lens')) {
        result = this.text(key.slice(0, -' · F1 Race Lens'.length)) + ' · F1 Race Lens';
      }
    }
    if (translated !== undefined && source !== key)
      result = (source.match(/^\s/) ? ' ' : '') + result + (source.match(/\s$/) ? ' ' : '');
    return parameters.length
      ? result.replace(/\{(\d+)\}/g, (_, index: string) => this.text(parameters[Number(index)]))
      : result;
  }

  private apply(value: Language): void {
    selected.set(value);
    this.document.documentElement.lang = value;
  }
}

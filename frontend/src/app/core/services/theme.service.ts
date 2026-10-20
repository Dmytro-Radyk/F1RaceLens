import { computed, inject, Service, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';

type Theme = 'dark' | 'light';

@Service()
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly storageKey = 'f1-race-lens-theme';
  private readonly selected = signal<Theme>(this.readPreference());

  readonly mode = this.selected.asReadonly();
  readonly isDark = computed(() => this.mode() === 'dark');

  toggle(): void {
    this.set(this.isDark() ? 'light' : 'dark');
  }

  set(next: Theme): void {
    this.selected.set(next);

    try {
      this.document.defaultView?.localStorage.setItem(this.storageKey, next);
    } catch {
      // The theme still works when browser storage is unavailable.
    }
  }

  private readPreference(): Theme {
    try {
      return this.document.defaultView?.localStorage.getItem(this.storageKey) === 'light'
        ? 'light'
        : 'dark';
    } catch {
      return 'dark';
    }
  }
}

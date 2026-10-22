import { LanguagePicker } from '../../core/i18n/language-picker';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import {
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
  ActivatedRoute,
  NavigationEnd,
} from '@angular/router';
import { accountError, AuthService } from '../../auth/services/auth.service';
import { ThemeService } from '../../core/services/theme.service';
import { AssistantService } from '../../features/assistant/assistant.service';

type NavId =
  | 'home'
  | 'assistant'
  | 'calendar'
  | 'standings'
  | 'drivers'
  | 'teams'
  | 'compare'
  | 'insights'
  | 'predictions'
  | 'news'
  | 'history';

interface NavItem {
  id: NavId;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-shell',
  imports: [LanguagePicker, TranslatePipe, RouterLink, RouterLinkActive, RouterOutlet],
  styleUrl: './app-shell.css',
  templateUrl: './app-shell.html',
  host: {
    '(document:keydown.escape)': 'closeMenu()',
    '(window:resize)': 'closeMenuOnDesktop()',
  },
})
export class AppShell {
  // Start ephemeral lifecycle handling even if this tab only performs sign-out.
  private readonly assistant = inject(AssistantService);
  protected readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly navigation = toSignal(
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    { initialValue: null },
  );
  protected readonly seasonQuery = computed(() => {
    this.navigation();
    const child = this.route.firstChild?.snapshot;
    const year = child?.paramMap.get('season') ?? child?.queryParamMap.get('season');
    const valid =
      year !== null &&
      year !== undefined &&
      /^\d{4}$/.test(year) &&
      Number(year) >= 1950 &&
      Number(year) <= new Date(Date.now()).getUTCFullYear() + 1;
    return { season: valid ? year : null };
  });
  protected readonly activeLinkOptions = {
    paths: 'subset',
    queryParams: 'ignored',
    matrixParams: 'ignored',
    fragment: 'ignored',
  } as const;
  protected readonly auth = inject(AuthService);
  protected readonly logoutError = signal('');
  protected readonly loggingOut = signal(false);
  protected readonly theme = inject(ThemeService);
  protected readonly menuItems: readonly NavItem[] = [
    { id: 'home', label: 'Головна', icon: '#icon-home' },
    { id: 'news', label: 'Новини F1', icon: '#icon-standings' },
    { id: 'calendar', label: 'Календар', icon: '#icon-calendar' },
    { id: 'standings', label: 'Турнірна таблиця', icon: '#icon-standings' },
    { id: 'drivers', label: 'Пілоти', icon: '#icon-drivers' },
    { id: 'teams', label: 'Команди', icon: '#icon-teams' },
    { id: 'compare', label: 'Порівняння', icon: '#icon-compare' },
    { id: 'insights', label: 'Аналітика', icon: '#icon-insights' },
    { id: 'predictions', label: 'Прогнози', icon: '#icon-predictions' },
    { id: 'history', label: 'Історія F1', icon: '#icon-calendar' },
    { id: 'assistant', label: 'F1-асистент', icon: '#icon-search' },
  ];

  protected readonly isMenuOpen = signal(false);
  protected readonly activeLabel = signal('Календар сезону');

  private readonly menuToggle = viewChild<ElementRef<HTMLButtonElement>>('menuToggle');
  private readonly sideNavigation = viewChild<ElementRef<HTMLElement>>('sideNavigation');

  protected onPageActivated(): void {
    this.activeLabel.set(this.route.firstChild?.snapshot.data['label'] ?? 'F1 Race Lens');
    this.closeMenu();
    setTimeout(() => {
      const fragment = this.route.firstChild?.snapshot.fragment;
      const target = fragment ? document.getElementById(fragment) : null;
      if (target) {
        target.scrollIntoView();
        target.querySelector<HTMLElement>('h3[tabindex]')?.focus({ preventScroll: true });
      } else document.getElementById('page-heading')?.focus();
    });
  }

  protected assistantPage(): string {
    const path = this.router.url.split(/[?#]/)[0];
    const season = this.router.parseUrl(this.router.url).queryParams['season'];
    return path + (/^\d{4}$/.test(season ?? '') ? '?season=' + season : '');
  }

  protected toggleMenu(): void {
    if (this.isMenuOpen()) {
      this.closeMenu();
      return;
    }

    this.isMenuOpen.set(true);
    setTimeout(() =>
      this.sideNavigation()?.nativeElement.querySelector<HTMLButtonElement>('button')?.focus(),
    );
  }

  protected closeMenu(): void {
    if (!this.isMenuOpen()) return;

    this.isMenuOpen.set(false);
    setTimeout(() => this.menuToggle()?.nativeElement.focus());
  }

  protected closeMenuOnDesktop(): void {
    if (window.innerWidth > 760) this.closeMenu();
  }

  protected trapMenuFocus(event: KeyboardEvent): void {
    if (!this.isMenuOpen() || event.key !== 'Tab') return;
    const buttons = this.sideNavigation()?.nativeElement.querySelectorAll<HTMLElement>(
      'button:not(:disabled), a[href]',
    );
    if (!buttons?.length) return;
    const first = buttons[0];
    const last = buttons[buttons.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  protected toggleTheme(): void {
    this.theme.toggle();
  }

  protected async logout(): Promise<void> {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.logoutError.set('');
    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/login');
      setTimeout(() => document.getElementById('login')?.focus());
    } catch (error) {
      this.logoutError.set(accountError(error));
    } finally {
      this.loggingOut.set(false);
    }
  }
}

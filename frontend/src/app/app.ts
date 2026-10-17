import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { AuthScreen } from './auth/components/auth-screen/auth-screen';
import { AuthService } from './auth/services/auth.service';
import { ThemeService } from './core/services/theme.service';

type NavId = 'home' | 'calendar' | 'standings' | 'drivers' | 'teams' | 'compare' | 'insights';

interface NavItem {
  id: NavId;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-root',
  imports: [AuthScreen],
  styleUrl: './app.css',
  templateUrl: './app.html',
  host: {
    '(document:keydown.escape)': 'closeMenu()',
    '(window:resize)': 'closeMenuOnDesktop()',
    '[attr.data-theme]': 'theme.mode()',
  },
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly menuItems: readonly NavItem[] = [
    { id: 'home', label: 'Головна', icon: '#icon-home' },
    { id: 'calendar', label: 'Календар', icon: '#icon-calendar' },
    { id: 'standings', label: 'Турнірна таблиця', icon: '#icon-standings' },
    { id: 'drivers', label: 'Пілоти', icon: '#icon-drivers' },
    { id: 'teams', label: 'Команди', icon: '#icon-teams' },
    { id: 'compare', label: 'Порівняння', icon: '#icon-compare' },
    { id: 'insights', label: 'Аналітика', icon: '#icon-insights' },
  ];

  protected readonly activeItem = signal<NavId>('home');
  protected readonly isMenuOpen = signal(false);
  protected readonly activeLabel = computed(
    () => this.menuItems.find((item) => item.id === this.activeItem())?.label ?? 'Головна',
  );

  private readonly menuToggle = viewChild<ElementRef<HTMLButtonElement>>('menuToggle');
  private readonly sideNavigation = viewChild<ElementRef<HTMLElement>>('sideNavigation');

  protected selectItem(id: NavId): void {
    this.activeItem.set(id);
    this.closeMenu();
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
    const buttons =
      this.sideNavigation()?.nativeElement.querySelectorAll<HTMLButtonElement>(
        'button:not(:disabled)',
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

  protected logout(): void {
    this.auth.logout();
    setTimeout(() => document.getElementById('login')?.focus());
  }
}

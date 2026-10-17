import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { AuthScreen } from './auth/components/auth-screen/auth-screen';
import { AuthService } from './auth/services/auth.service';

type NavId =
  | 'home'
  | 'calendar'
  | 'standings'
  | 'drivers'
  | 'teams'
  | 'compare'
  | 'insights';

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
  },
})
export class App {
  protected readonly auth = inject(AuthService);
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
  protected readonly isDarkTheme = signal(false);
  protected readonly isMenuOpen = signal(false);
  protected readonly activeLabel = computed(
    () => this.menuItems.find((item) => item.id === this.activeItem())?.label ?? 'Головна',
  );

  private readonly menuToggle = viewChild<ElementRef<HTMLButtonElement>>('menuToggle');

  protected selectItem(id: NavId): void {
    this.activeItem.set(id);
    this.closeMenu();
  }

  protected toggleMenu(): void {
    this.isMenuOpen.update((isOpen) => !isOpen);
  }

  protected closeMenu(): void {
    if (!this.isMenuOpen()) return;

    this.isMenuOpen.set(false);
    this.menuToggle()?.nativeElement.focus();
  }

  protected toggleTheme(): void {
    this.isDarkTheme.update((isDark) => !isDark);
  }

  protected logout(): void {
    this.auth.logout();
    setTimeout(() => document.getElementById('login')?.focus());
  }
}

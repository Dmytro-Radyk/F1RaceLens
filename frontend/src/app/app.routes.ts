import { Routes } from '@angular/router';
import { accountGuard } from './auth/services/account.guard';

export const routes: Routes = [
  ...(['confirm-email', 'confirm-email-change'] as const).map((path) => ({
    path,
    data: { emailMode: path },
    loadComponent: () =>
      import('./auth/components/auth-screen/auth-screen').then((m) => m.AuthScreen),
  })),
  ...(['forgot', 'reset'] as const).map((mode) => ({
    path: mode + '-password',
    data: { passwordMode: mode },
    loadComponent: () =>
      import('./auth/components/auth-screen/auth-screen').then((m) => m.AuthScreen),
  })),
  {
    path: 'login',
    loadComponent: () =>
      import('./auth/components/auth-screen/auth-screen').then((m) => m.AuthScreen),
  },
  {
    path: '',
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: 'assistant',
        title: 'F1-асистент · F1 Race Lens',
        data: { label: 'F1-асистент' },
        loadComponent: () => import('./features/assistant/f1-assistant').then((m) => m.F1Assistant),
      },
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      {
        path: 'search',
        title: 'Пошук · F1 Race Lens',
        data: { label: 'Пошук' },
        loadComponent: () => import('./features/search/site-search').then((m) => m.SiteSearch),
      },
      {
        path: 'history',
        title: 'Історія F1 · F1 Race Lens',
        data: { label: 'Історія F1' },
        loadComponent: () => import('./features/history/f1-history').then((m) => m.F1History),
      },
      {
        path: 'drivers/career/:id',
        title: 'Кар’єра пілота · F1 Race Lens',
        data: { label: 'Кар’єра пілота', catalogKind: 'drivers' },
        loadComponent: () =>
          import('./features/catalog/pages/career-profile').then((m) => m.CareerProfile),
      },
      {
        path: 'teams/career/:id',
        title: 'Історія конструктора · F1 Race Lens',
        data: { label: 'Історія конструктора', catalogKind: 'teams' },
        loadComponent: () =>
          import('./features/catalog/pages/career-profile').then((m) => m.CareerProfile),
      },
      {
        path: 'news',
        title: 'Новини F1 · F1 Race Lens',
        data: { label: 'Новини F1' },
        loadComponent: () => import('./features/news/f1-news').then((m) => m.F1News),
      },
      {
        path: 'profile',
        canActivate: [accountGuard],
        title: 'Мій профіль · F1 Race Lens',
        data: { label: 'Мій профіль' },
        loadComponent: () => import('./features/profile/user-profile').then((m) => m.UserProfile),
      },
      {
        path: 'calendar',
        title: 'Календар сезону · F1 Race Lens',
        data: { label: 'Календар сезону' },
        loadComponent: () =>
          import('./features/calendar/pages/season-calendar/season-calendar').then(
            (m) => m.SeasonCalendar,
          ),
      },
      {
        path: 'calendar/:season/:round',
        title: 'Гран-прі · F1 Race Lens',
        data: { label: 'Гран-прі' },
        loadComponent: () =>
          import('./features/calendar/pages/grand-prix/grand-prix').then((m) => m.GrandPrix),
      },
      {
        path: 'standings',
        title: 'Турнірна таблиця · F1 Race Lens',
        data: { label: 'Турнірна таблиця' },
        loadComponent: () =>
          import('./features/standings/season-standings').then((m) => m.SeasonStandings),
      },
      {
        path: 'teams',
        title: 'Команди · F1 Race Lens',
        data: { label: 'Команди', catalogKind: 'teams' },
        loadComponent: () =>
          import('./features/catalog/pages/career-directory').then((m) => m.CareerDirectory),
      },
      {
        path: 'teams/:teamId',
        title: 'Профіль команди · F1 Race Lens',
        data: { label: 'Профіль команди' },
        loadComponent: () => import('./features/teams/season-teams').then((m) => m.SeasonTeams),
      },
      {
        path: 'drivers',
        title: 'Пілоти · F1 Race Lens',
        data: { label: 'Пілоти', catalogKind: 'drivers' },
        loadComponent: () =>
          import('./features/catalog/pages/career-directory').then((m) => m.CareerDirectory),
      },
      {
        path: 'drivers/:driverId',
        title: 'Профіль пілота · F1 Race Lens',
        data: { label: 'Профіль пілота' },
        loadComponent: () =>
          import('./features/drivers/pages/driver-profile').then((m) => m.DriverProfile),
      },
      {
        path: 'home',
        title: 'Головна · F1 Race Lens',
        data: { label: 'Головна' },
        loadComponent: () => import('./features/home/home-overview').then((m) => m.HomeOverview),
      },
      {
        path: 'compare',
        title: 'Порівняння пілотів · F1 Race Lens',
        data: { label: 'Порівняння пілотів' },
        loadComponent: () =>
          import('./features/compare/driver-comparison').then((m) => m.DriverComparison),
      },
      {
        path: 'insights',
        title: 'Аналітика пілота · F1 Race Lens',
        data: { label: 'Аналітика пілота' },
        loadComponent: () =>
          import('./features/insights/driver-analytics').then((m) => m.DriverAnalytics),
      },
      {
        path: 'predictions',
        title: 'Прогнози сезону · F1 Race Lens',
        data: { label: 'Прогнози сезону' },
        loadComponent: () =>
          import('./features/predictions/season-predictions').then((m) => m.SeasonPredictions),
      },
      {
        path: '**',
        title: 'Сторінку не знайдено · F1 Race Lens',
        data: { label: 'Сторінку не знайдено', notFound: true },
        loadComponent: () =>
          import('./shared/pages/section-placeholder').then((m) => m.SectionPlaceholder),
      },
    ],
  },
];

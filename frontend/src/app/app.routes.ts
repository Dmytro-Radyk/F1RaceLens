import { Routes } from '@angular/router';
import { accountGuard } from './auth/services/account.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./auth/components/auth-screen/auth-screen').then((m) => m.AuthScreen),
  },
  {
    path: '',
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
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
        data: { label: 'Команди' },
        loadComponent: () => import('./features/teams/season-teams').then((m) => m.SeasonTeams),
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
        data: { label: 'Пілоти' },
        loadComponent: () =>
          import('./features/drivers/pages/drivers-list').then((m) => m.DriversList),
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

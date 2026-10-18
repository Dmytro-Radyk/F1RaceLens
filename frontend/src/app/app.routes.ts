import { Routes } from '@angular/router';

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
      { path: '', pathMatch: 'full', redirectTo: 'calendar' },
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
      ...[
        ['home', 'Головна'],
        ['compare', 'Порівняння'],
        ['insights', 'Аналітика'],
      ].map(([path, label]) => ({
        path,
        title: `${label} · F1 Race Lens`,
        data: { label },
        loadComponent: () =>
          import('./shared/pages/section-placeholder').then((m) => m.SectionPlaceholder),
      })),
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

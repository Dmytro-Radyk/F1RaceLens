import { LocaleService } from './core/i18n/locale.service';
import { LocalizedTitle } from './core/i18n/localized-title';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, TitleStrategy } from '@angular/router';
import { routes } from './app.routes';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(() => inject(LocaleService).ready),
    { provide: TitleStrategy, useClass: LocalizedTitle },
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
  ],
};

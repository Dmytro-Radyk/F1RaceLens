import { LocaleService } from './core/i18n/locale.service';
import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/services/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css',
  host: { '[attr.data-theme]': 'theme.mode()' },
})
export class App {
  protected readonly locale = inject(LocaleService);
  protected readonly theme = inject(ThemeService);
}

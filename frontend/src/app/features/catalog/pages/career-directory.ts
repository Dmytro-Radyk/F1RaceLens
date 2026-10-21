import { Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { currentLocale } from '../../../core/i18n/locale.service';
import { CareerCatalog, sortCareer } from '../catalog.service';
import { DriversList } from '../../drivers/pages/drivers-list';
import { SeasonTeams } from '../../teams/season-teams';

@Component({
  selector: 'app-career-directory',
  imports: [TranslatePipe, RouterLink, DriversList, SeasonTeams],
  templateUrl: './career-directory.html',
  styleUrl: '../catalog.css',
})
export class CareerDirectory {
  private readonly route = inject(ActivatedRoute);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly kind =
    this.route.snapshot.data['catalogKind'] === 'teams' ? 'teams' : 'drivers';
  protected readonly seasonMode = computed(
    () => this.query().get('view') === 'season' || this.query().has('season'),
  );
  private readonly source = inject(CareerCatalog);
  protected readonly catalog = this.source.data;
  constructor() {
    effect(() => {
      if (!this.seasonMode()) this.source.load();
    });
  }
  protected readonly filter = signal('');
  protected readonly sort = signal('titles');
  protected readonly limit = signal(40);
  protected readonly entries = computed(() =>
    this.catalog.hasValue()
      ? sortCareer(
          this.kind === 'teams' ? this.catalog.value().constructors : this.catalog.value().drivers,
          this.filter(),
          this.sort(),
          currentLocale(),
        )
      : [],
  );
  protected readonly visible = computed(() => this.entries().slice(0, this.limit()));
  protected filterChanged(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
    this.limit.set(40);
  }
  protected sortChanged(event: Event): void {
    this.sort.set((event.target as HTMLSelectElement).value);
    this.limit.set(40);
  }
  protected number(value: number): string {
    return new Intl.NumberFormat(currentLocale()).format(value);
  }
  protected country(code: string | null): string {
    return code
      ? (new Intl.DisplayNames([currentLocale()], { type: 'region' }).of(code) ?? code)
      : '—';
  }
}

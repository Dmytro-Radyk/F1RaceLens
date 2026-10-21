import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { currentLocale } from '../../../core/i18n/locale.service';
import { CareerCatalog } from '../catalog.service';

@Component({
  selector: 'app-career-profile',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './career-profile.html',
  styleUrl: '../catalog.css',
})
export class CareerProfile {
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  protected readonly kind =
    this.route.snapshot.data['catalogKind'] === 'teams' ? 'teams' : 'drivers';
  protected readonly catalog = inject(CareerCatalog).data;
  constructor() {
    inject(CareerCatalog).load();
  }
  protected readonly entry = computed(() =>
    this.catalog.hasValue()
      ? (this.kind === 'teams'
          ? this.catalog.value().constructors
          : this.catalog.value().drivers
        ).find((row) => row.id === this.params().get('id'))
      : undefined,
  );
  protected readonly related = computed(() => {
    if (this.kind !== 'teams' || !this.catalog.hasValue() || !this.entry()) return [];
    const data = this.catalog.value(),
      id = this.entry()!.id;
    return data.lineage
      .filter((row) => row.parentConstructorId === id || row.constructorId === id)
      .map((row) => ({
        ...row,
        name:
          data.constructors.find((team) => team.id === row.constructorId)?.name ??
          row.constructorId,
      }));
  });
  protected country(code: string | null): string {
    return code
      ? (new Intl.DisplayNames([currentLocale()], { type: 'region' }).of(code) ?? code)
      : '—';
  }
  protected number(value: number): string {
    return new Intl.NumberFormat(currentLocale()).format(value);
  }
  protected date(value: string): string {
    return new Intl.DateTimeFormat(currentLocale(), { dateStyle: 'long', timeZone: 'UTC' }).format(
      new Date(value),
    );
  }
}

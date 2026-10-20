import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { LocaleService } from '../../../core/i18n/locale.service';
import { RaceStatus } from '../../../core/i18n/race-status';
import type { DriverRace } from '../models/driver-profile.types';
import { buildFormChart } from '../utils/form-chart';

@Component({
  imports: [TranslatePipe],
  selector: 'app-form-chart',
  templateUrl: './form-chart.html',
  styleUrl: './form-chart.css',
  host: {
    '(document:keydown.escape)': 'dismiss()',
    '(document:pointerdown)': 'dismissOutside($event)',
    '(window:resize)': 'dismiss()',
  },
})
export class FormChart {
  private readonly locale = inject(LocaleService);
  private readonly status = inject(RaceStatus);
  readonly races = input.required<readonly DriverRace[]>();
  protected readonly chart = computed(() => buildFormChart(this.races()));
  protected readonly hovered = linkedSignal<string | null>(() => {
    this.races();
    return null;
  });
  protected readonly focused = linkedSignal<string | null>(() => {
    this.races();
    return null;
  });
  protected readonly active = computed(() => this.hovered() ?? this.focused());
  private readonly placements = signal<
    Record<string, { left: number; top: number | null; bottom: number | null }>
  >({});
  protected readonly placement = computed(() => this.placements()[this.active() ?? '']);
  protected readonly selected = computed(() =>
    this.markers().find((marker) => marker.id === this.active()),
  );
  protected readonly markers = computed(() =>
    this.chart().points.flatMap((point) => {
      const entries = this.races().filter((race) => race.round === point.round);
      const race = entries[0];
      const title = this.locale.text('Етап {0} · {1}', [point.round, race.name]);
      const grid = race.result.grid;
      const start =
        grid === null
          ? this.locale.text('Старт невідомий')
          : grid === 0
            ? this.locale.text('Старт: піт-лейн / невідомо')
            : this.locale.text('Старт: {0}', [grid]);
      const detail =
        entries.length > 1
          ? this.locale.text('Кілька записів одного етапу — деталі у таблиці.')
          : [
              start,
              point.finished
                ? this.locale.text('Фініш: {0}', [race.result.position])
                : this.status.text(race.result.status),
            ].join(' · ');
      const both = point.finished && point.gridY === point.y;
      const positions = both
        ? [{ kind: 'both', y: point.y, label: 'Старт і фініш' }]
        : [
            ...(point.gridY === null
              ? []
              : [{ kind: 'start', y: point.gridY, label: 'Точка старту' }]),
            { kind: 'finish', y: point.y, label: point.finished ? 'Точка фінішу' : 'Статус етапу' },
          ];
      return positions.map((position) => ({
        id: `${point.round}-${position.kind}`,
        x: point.x,
        y: position.y,
        title,
        detail,
        label: [this.locale.text(position.label), title, detail].join('. '),
      }));
    }),
  );
  protected dismiss() {
    this.hovered.set(null);
    this.focused.set(null);
  }
  protected show(id: string, event: Event, focus = false) {
    const target = event.currentTarget as HTMLElement;
    this.place(id, target);
    if (focus) {
      this.hovered.set(null);
      this.focused.set(id);
    } else this.hovered.set(id);
  }
  private place(id: string, target: HTMLElement) {
    const frame = target.closest('.chart-frame')!.getBoundingClientRect();
    const point = target.getBoundingClientRect();
    const x = point.left + point.width / 2 - frame.left;
    const y = point.top + point.height / 2 - frame.top;
    const width = Math.min(220, frame.width - 16);
    const below = y < frame.height / 2;
    this.placements.update((positions) => ({
      ...positions,
      [id]: {
        left: Math.max(8, Math.min(x - width / 2, frame.width - width - 8)),
        top: below ? y + 20 : null,
        bottom: below ? null : frame.height - y + 20,
      },
    }));
  }
  protected onChartScroll(event: Event) {
    this.hovered.set(null);
    const scroller = event.currentTarget as HTMLElement;
    const target = document.activeElement;
    const id = this.focused();
    if (
      !id ||
      !(target instanceof HTMLElement) ||
      !target.matches('.point-target') ||
      !scroller.contains(target)
    ) {
      this.focused.set(null);
      return;
    }
    const point = target.getBoundingClientRect();
    const viewport = scroller.getBoundingClientRect();
    const center = point.left + point.width / 2;
    if (center < viewport.left || center > viewport.right) this.focused.set(null);
    else this.place(id, target);
  }
  protected dismissOutside(event: Event) {
    if (!(event.target instanceof Element) || !event.target.closest('.chart-point, .chart-tooltip'))
      this.dismiss();
  }
}

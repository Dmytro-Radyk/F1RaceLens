import { computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

export function createSeasonContext() {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const query = toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap });
  const currentYear = new Date(Date.now()).getUTCFullYear();
  const season = computed(() => query().get('season') ?? 'current');
  const year = computed(() => (season() === 'current' ? currentYear : Number(season())));
  const valid = computed(
    () =>
      season() === 'current' ||
      (/^\d{4}$/.test(season()) && year() >= 1950 && year() <= currentYear),
  );
  return {
    season,
    year,
    valid,
    select: (selected: string) => {
      void router.navigate([], {
        relativeTo: route,
        queryParams: { season: selected === String(currentYear) ? null : selected },
        queryParamsHandling: 'merge',
      });
    },
  };
}

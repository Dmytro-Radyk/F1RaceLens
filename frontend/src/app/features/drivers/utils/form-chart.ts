import type { DriverRace } from '../models/driver-profile.types';

export function isFinish(race: DriverRace): boolean {
  return race.result.status === 'Finished' || /^\+\d+ Laps?$/.test(race.result.status ?? '');
}

export function buildFormChart(races: readonly DriverRace[]) {
  const lastRound = Math.max(1, ...races.map((race) => race.round));
  const maxPosition = Math.max(
    10,
    ...races.flatMap((race) => [race.result.position, race.result.grid ?? 0]),
  );
  const x = (round: number) => (lastRound === 1 ? 400 : 55 + ((round - 1) * 690) / (lastRound - 1));
  const y = (position: number) => 25 + ((position - 1) * 230) / (maxPosition - 1);
  const points: {
    round: number;
    x: number;
    y: number;
    gridY: number | null;
    label: string;
    finished: boolean;
  }[] = [];
  let start = '',
    finish = '',
    startOpen = false,
    finishOpen = false;
  for (let round = 1; round <= lastRound; round++) {
    const entries = races.filter((race) => race.round === round);
    if (entries.length !== 1) {
      startOpen = finishOpen = false;
      if (entries.length > 1)
        points.push({
          round,
          x: x(round),
          y: 290,
          gridY: null,
          label: `Етап ${round}: кілька записів результату, дивіться таблицю`,
          finished: false,
        });
      continue;
    }
    const race = entries[0];
    const finished = isFinish(race);
    const grid = race.result.grid !== null && race.result.grid > 0 ? race.result.grid : null;
    if (grid !== null) {
      start += `${startOpen ? 'L' : 'M'}${x(round)},${y(grid)} `;
      startOpen = true;
    } else startOpen = false;
    if (finished) {
      finish += `${finishOpen ? 'L' : 'M'}${x(round)},${y(race.result.position)} `;
      finishOpen = true;
    } else finishOpen = false;
    points.push({
      round,
      x: x(round),
      y: finished ? y(race.result.position) : 290,
      gridY: grid === null ? null : y(grid),
      label: `${race.name}: старт ${grid ?? 'невідомий'}, класифікація ${race.result.position}, статус ${race.result.status ?? 'невідомий'}`,
      finished,
    });
  }
  const ticks = [1, ...Array.from({ length: Math.floor(maxPosition / 5) }, (_, i) => (i + 1) * 5)];
  return { start, finish, points, ticks: ticks.map((value) => ({ value, y: y(value) })) };
}

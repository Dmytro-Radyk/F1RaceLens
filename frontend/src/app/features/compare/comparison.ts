import type { DriverProfileResponse, DriverRace } from '../drivers/models/driver-profile.types';
import { isFinish } from '../drivers/utils/form-chart';

export function compareRaces(left: DriverProfileResponse, right: DriverProfileResponse) {
  const rounds = [...new Set([...left.races, ...right.races].map((race) => race.round))].sort(
    (a, b) => a - b,
  );
  const rows = rounds.map((round) => {
    const a = left.races.filter((race) => race.round === round);
    const b = right.races.filter((race) => race.round === round);
    const comparable = a.length === 1 && b.length === 1 && isFinish(a[0]) && isFinish(b[0]);
    return {
      round,
      name: (a[0] ?? b[0]).name,
      left: a,
      right: b,
      winner: !comparable
        ? null
        : a[0].result.position < b[0].result.position
          ? 'left'
          : a[0].result.position > b[0].result.position
            ? 'right'
            : 'tie',
    };
  });
  const summary = (races: readonly DriverRace[]) => {
    const unique = races.filter(
      (race) => races.filter((other) => other.round === race.round).length === 1,
    );
    const finishes = unique.filter(isFinish);
    return {
      finishes: finishes.length,
      average: finishes.length
        ? finishes.reduce((sum, race) => sum + race.result.position, 0) / finishes.length
        : null,
    };
  };
  return {
    rows,
    left: summary(left.races),
    right: summary(right.races),
    leftWins: rows.filter((row) => row.winner === 'left').length,
    rightWins: rows.filter((row) => row.winner === 'right').length,
    ties: rows.filter((row) => row.winner === 'tie').length,
    comparable: rows.filter((row) => row.winner !== null).length,
  };
}

export function resultLabel(races: readonly DriverRace[]): string {
  if (!races.length) return 'Немає запису';
  return races
    .map((race) =>
      isFinish(race)
        ? `Фініш: ${race.result.position}`
        : `Класифікація: ${race.result.position}; статус: ${race.result.status ?? 'невідомий'}`,
    )
    .join(' / ');
}

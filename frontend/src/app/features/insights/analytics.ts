import type { DriverRace } from '../drivers/models/driver-profile.types';
import { isFinish } from '../drivers/utils/form-chart';

export function analyseRaces(races: readonly DriverRace[], window: number) {
  const rounds = [...new Set(races.map((race) => race.round))].sort((a, b) => b - a);
  const events = rounds.map((round) => {
    const entries = races.filter((race) => race.round === round);
    const unique = entries.length === 1 ? entries[0] : null;
    const finished = unique !== null && isFinish(unique);
    const gain =
      finished && unique!.result.grid !== null && unique!.result.grid > 0
        ? unique!.result.grid - unique!.result.position
        : null;
    return { round, name: entries[0].name, entries, unique, finished, gain };
  });
  const summarise = (rows: typeof events) => {
    const unique = rows.filter((row) => row.unique !== null);
    const finishes = rows.filter((row) => row.finished);
    const gains = rows.flatMap((row) => (row.gain === null ? [] : [row.gain]));
    return {
      events: rows,
      recorded: rows.length,
      eligible: unique.length,
      ambiguous: rows.length - unique.length,
      finishes: finishes.length,
      average: finishes.length
        ? finishes.reduce((sum, row) => sum + row.unique!.result.position, 0) / finishes.length
        : null,
      best: finishes.length
        ? Math.min(...finishes.map((row) => row.unique!.result.position))
        : null,
      gainSample: gains.length,
      averageGain: gains.length ? gains.reduce((sum, gain) => sum + gain, 0) / gains.length : null,
      otherStatuses: unique.length - finishes.length,
    };
  };
  const recent = summarise(events.slice(0, window));
  const previous = summarise(events.slice(window, window * 2));
  const comparable =
    recent.recorded === window &&
    previous.recorded === window &&
    recent.finishes >= 3 &&
    previous.finishes >= 3;
  const improvement = comparable ? previous.average! - recent.average! : null;
  const scale = Math.max(1, ...recent.events.map((row) => Math.abs(row.gain ?? 0)));
  return { recent, previous, improvement, scale };
}

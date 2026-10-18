import type { DriverResult } from '../../calendar/models/results.types';
import type { StandingsEntry } from '../../standings/standings.types';

export interface DriverRace {
  readonly round: number;
  readonly name: string;
  readonly date: string;
  readonly circuit: string;
  readonly result: DriverResult;
}
export interface DriverProfileResponse {
  readonly season: number;
  readonly updatedAt: string;
  readonly isStale: boolean;
  readonly source: string;
  readonly driver: {
    readonly id: string;
    readonly name: string;
    readonly code: string | null;
    readonly dateOfBirth: string | null;
    readonly nationality: string | null;
  };
  readonly standingsRound: number | null;
  readonly standing: StandingsEntry | null;
  readonly races: readonly DriverRace[];
}

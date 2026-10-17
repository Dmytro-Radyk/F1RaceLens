export interface StandingsEntry {
  readonly position: number | null;
  readonly positionText: string;
  readonly id: string;
  readonly name: string;
  readonly code: string | null;
  readonly teams: readonly string[];
  readonly points: number;
  readonly wins: number;
}
export interface StandingsResponse {
  readonly season: number;
  readonly round: number | null;
  readonly kind: 'drivers' | 'constructors';
  readonly updatedAt: string;
  readonly isStale: boolean;
  readonly source: string;
  readonly entries: readonly StandingsEntry[];
}

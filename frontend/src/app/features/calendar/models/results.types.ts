import type { ScheduleMetadata } from './calendar.types';

export type ResultKind = 'race' | 'qualifying' | 'sprint';

export interface DriverResult {
  readonly position: number;
  readonly positionText: string | null;
  readonly driverId: string;
  readonly driverName: string;
  readonly driverCode: string | null;
  readonly team: string | null;
  readonly grid: number | null;
  readonly laps: number | null;
  readonly time: string | null;
  readonly status: string | null;
  readonly points: number | null;
  readonly q1: string | null;
  readonly q2: string | null;
  readonly q3: string | null;
}

export interface SessionResultsResponse extends ScheduleMetadata {
  readonly round: number;
  readonly kind: ResultKind;
  readonly entries: readonly DriverResult[];
}

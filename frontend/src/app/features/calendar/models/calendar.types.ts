export interface SessionSchedule {
  readonly kind: string;
  readonly name: string;
  readonly date: string;
  readonly startsAt: string | null;
}

export interface RaceSchedule {
  readonly season: number;
  readonly round: number;
  readonly name: string;
  readonly circuit: {
    readonly id: string;
    readonly name: string;
    readonly locality: string;
    readonly country: string;
  };
  readonly date: string;
  readonly startsAt: string | null;
  readonly sessions: readonly SessionSchedule[];
}

export interface ScheduleMetadata {
  readonly season: number;
  readonly updatedAt: string;
  readonly isStale: boolean;
  readonly source: string;
}

export interface CalendarResponse extends ScheduleMetadata {
  readonly races: readonly RaceSchedule[];
}

export interface RaceResponse extends ScheduleMetadata {
  readonly race: RaceSchedule;
}

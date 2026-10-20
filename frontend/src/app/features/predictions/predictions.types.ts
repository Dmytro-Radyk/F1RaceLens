export interface ForecastRow {
  readonly id: string;
  readonly name: string;
  readonly currentPosition: number;
  readonly currentPoints: number;
  readonly active: boolean;
  readonly expectedPoints: number;
  readonly pointsLow: number;
  readonly pointsHigh: number;
  readonly averagePosition: number;
  readonly positionLow: number;
  readonly positionHigh: number;
  readonly titleChance: number;
  readonly top3Chance: number;
}
export interface PredictionResponse {
  readonly generatedAt?: string | null;
  readonly recordedAt?: string | null;
  readonly runId?: string | null;
  readonly races?: readonly RaceForecast[] | null;
  readonly season: number;
  readonly updatedAt: string;
  readonly isStale: boolean;
  readonly source: string;
  readonly status: string;
  readonly message: string | null;
  readonly throughRound: number;
  readonly remainingRaces: number;
  readonly remainingSprints: number;
  readonly simulations: number;
  readonly modelVersion: string;
  readonly drivers: readonly ForecastRow[];
  readonly teams: readonly ForecastRow[];
  readonly factors?: PredictionFactors | null;
}
export interface RaceForecast {
  readonly round: number;
  readonly name: string;
  readonly date: string;
  readonly drivers: readonly {
    readonly id: string;
    readonly name: string;
    readonly expectedPoints: number;
    readonly averagePosition: number;
    readonly winChance: number;
    readonly podiumChance: number;
    readonly nonClassificationChance: number;
  }[];
}
export interface ForecastScenario {
  readonly weather: 'auto' | 'dry' | 'wet';
  readonly developmentTeamId: string | null;
  readonly developmentPositions: number;
  readonly replacement: {
    readonly outgoingId: string;
    readonly incomingId: string;
    readonly fromRound: number;
  } | null;
}
export interface CircuitFactor {
  readonly round: number;
  readonly sprint: boolean;
  readonly circuitId: string;
  readonly name: string;
  readonly date: string;
  readonly historicalRaces: number;
  readonly averageGridChange: number | null;
  readonly nonClassificationRate: number | null;
  readonly rainProbability: number | null;
  readonly temperature: number | null;
  readonly wind: number | null;
  readonly weatherStatus: string;
  readonly weatherUpdatedAt: string | null;
}
export interface PredictionFactors {
  readonly events: readonly CircuitFactor[];
  readonly historicalSeasons: readonly number[];
  readonly isScenario: boolean;
  readonly settings: ForecastScenario;
  readonly rosterIds: readonly string[];
}

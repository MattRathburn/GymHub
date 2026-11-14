/** Mirrors GymHub.Modules.TrainingLog DTOs. Enums are sent as strings; dates are ISO yyyy-mm-dd. */

import { ExerciseSet, Workout } from '../programs/program.model';

export type RecordKind = 'Weight' | 'E1Rm';

export interface LoggedSet {
  reps: number | null;
  weightKg: number | null;
  durationSec: number | null;
  distanceM: number | null;
  rpe: number | null;
}

export interface LoggedExerciseRequest {
  name: string;
  programExerciseId: string | null;
  notes: string | null;
  sets: LoggedSet[];
}

export interface SessionRequest {
  performedOn: string;
  title: string | null;
  notes: string | null;
  durationMin: number | null;
  programId: string | null;
  programWorkoutId: string | null;
  exercises: LoggedExerciseRequest[];
}

export interface PersonalRecord {
  exerciseKey: string;
  name: string;
  kind: RecordKind;
  value: number;
  previous: number;
}

export interface SavedSession {
  id: string;
  records: PersonalRecord[];
}

export interface SessionSummary {
  id: string;
  performedOn: string;
  title: string;
  programId: string | null;
  programTitle: string | null;
  weekNumber: number | null;
  durationMin: number | null;
  exerciseCount: number;
  setCount: number;
  volumeKg: number;
  exerciseNames: string[];
}

export interface LoggedExercise {
  id: string;
  order: number;
  name: string;
  exerciseKey: string;
  programExerciseId: string | null;
  notes: string | null;
  sets: LoggedSet[];
  topWeightKg: number | null;
  bestE1RmKg: number | null;
  volumeKg: number;
}

export interface Session {
  id: string;
  performedOn: string;
  title: string;
  notes: string | null;
  durationMin: number | null;
  programId: string | null;
  programTitle: string | null;
  weekNumber: number | null;
  programWorkoutId: string | null;
  createdAt: string;
  updatedAt: string;
  exercises: LoggedExercise[];
}

export interface ExerciseSummary {
  key: string;
  name: string;
  lastPerformedOn: string;
  sessionCount: number;
  bestWeightKg: number | null;
  bestE1RmKg: number | null;
}

export interface ProgressPoint {
  sessionId: string;
  performedOn: string;
  topWeightKg: number | null;
  bestE1RmKg: number | null;
  volumeKg: number;
  totalReps: number;
}

export interface RecordHistory {
  sessionId: string;
  performedOn: string;
  kind: RecordKind;
  value: number;
  previous: number;
}

export interface ExerciseProgress {
  key: string;
  name: string;
  bestWeightKg: number | null;
  bestE1RmKg: number | null;
  points: ProgressPoint[];
  records: RecordHistory[];
}

export interface AdherenceWorkout {
  workoutId: string;
  dayNumber: number;
  title: string;
  lastSessionId: string | null;
  lastPerformedOn: string | null;
}

export interface AdherenceWeek {
  weekId: string;
  weekNumber: number;
  title: string;
  releasesOn: string;
  planned: number;
  completed: number;
  workouts: AdherenceWorkout[];
}

export interface Adherence {
  programId: string;
  planned: number;
  completed: number;
  weeks: AdherenceWeek[];
  noLongerInProgram: { sessionId: string; performedOn: string; title: string; weekNumber: number | null }[];
}

export type ProgressMetric = 'bestE1RmKg' | 'topWeightKg' | 'volumeKg';

export const metricLabels: Record<ProgressMetric, string> = {
  bestE1RmKg: 'Estimated 1RM (kg)',
  topWeightKg: 'Top weight (kg)',
  volumeKg: 'Volume (kg)',
};

/** Same rule as the server: lower-case letters and digits, any other run of characters as one hyphen. */
export function normalizeExerciseKey(name: string | null | undefined): string {
  return (name ?? '')
    .trim()
    .toLowerCase()
    .split(/[^\p{L}\p{N}]+/u)
    .filter(Boolean)
    .join('-');
}

/** Epley, as on the server: only for 1 to 12 reps with a weight. */
export function estimatedOneRepMax(reps: number | null, weightKg: number | null): number | null {
  if (reps === null || weightKg === null || weightKg <= 0 || reps < 1 || reps > 12) {
    return null;
  }

  return reps === 1 ? weightKg : Math.round(weightKg * (1 + reps / 30) * 10) / 10;
}

export function emptySet(): LoggedSet {
  return { reps: null, weightKg: null, durationSec: null, distanceM: null, rpe: null };
}

export function hasAnyValue(set: LoggedSet): boolean {
  return set.reps !== null || set.weightKg !== null || set.durationSec !== null || set.distanceM !== null;
}

/** A program workout as a starting point for the log form: its exercises with the planned sets. */
export function draftFromWorkout(workout: Workout): LoggedExerciseRequest[] {
  return workout.exercises.map((exercise) => ({
    name: exercise.name,
    programExerciseId: exercise.id,
    notes: null,
    sets: exercise.sets.length > 0 ? exercise.sets.map(fromPlanned) : [emptySet()],
  }));
}

function fromPlanned(set: ExerciseSet): LoggedSet {
  return { reps: set.reps, weightKg: set.weightKg, durationSec: set.durationSec, distanceM: set.distanceM, rpe: null };
}

/** "5 × 100 kg", "60 s", "5 km". */
export function describeSet(set: LoggedSet): string {
  const parts = [
    set.reps !== null && set.weightKg !== null ? `${set.reps} × ${set.weightKg} kg` : null,
    set.reps !== null && set.weightKg === null ? `${set.reps} reps` : null,
    set.reps === null && set.weightKg !== null ? `${set.weightKg} kg` : null,
    set.durationSec !== null ? `${set.durationSec} s` : null,
    set.distanceM !== null ? (set.distanceM >= 1000 ? `${set.distanceM / 1000} km` : `${set.distanceM} m`) : null,
    set.rpe !== null ? `RPE ${set.rpe}` : null,
  ];

  return parts.filter(Boolean).join(' · ');
}

export function recordLabel(kind: RecordKind): string {
  return kind === 'Weight' ? 'Heaviest weight' : 'Estimated 1RM';
}

/** Today in the member's own time zone, as yyyy-mm-dd. */
export function localToday(now = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export interface ChartPoint {
  x: number;
  y: number;
  value: number;
  date: string;
}

/**
 * Lays out a metric over time in a width × height box, by date. Points without the metric are skipped. The y axis
 * starts a little under the lowest value so small changes stay visible.
 */
export function chartPoints(points: ProgressPoint[], metric: ProgressMetric, width: number, height: number): ChartPoint[] {
  const values = points
    .map((p) => ({ date: p.performedOn, value: p[metric] }))
    .filter((p): p is { date: string; value: number } => p.value !== null && p.value !== undefined);

  if (values.length === 0) {
    return [];
  }

  const times = values.map((v) => Date.parse(v.date));
  const minTime = Math.min(...times);
  const timeSpan = Math.max(...times) - minTime;
  const max = Math.max(...values.map((v) => v.value));
  const min = Math.min(...values.map((v) => v.value));
  const floor = Math.max(0, min - (max - min) * 0.1 - (max === min ? max * 0.1 : 0));
  const span = max - floor || 1;

  return values.map((v, i) => ({
    x: timeSpan === 0 ? (values.length === 1 ? width / 2 : (i / (values.length - 1)) * width) : ((times[i] - minTime) / timeSpan) * width,
    y: height - ((v.value - floor) / span) * height,
    value: v.value,
    date: v.date,
  }));
}

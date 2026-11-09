/** Mirrors GymHub.Modules.Programs DTOs. Enums are sent as strings. */

export type ProgramKind = 'Finite' | 'Ongoing';
export type ProgramStatus = 'Draft' | 'Published' | 'Archived';
export type PricingModel = 'Free' | 'OneTime' | 'Recurring';
export type BillingInterval = 'Monthly' | 'Yearly';
export type MediaTarget = 'Cover' | 'Program' | 'Week' | 'Day' | 'Workout' | 'Exercise';

export interface ProgramPrice {
  model: PricingModel;
  amountMinor: number | null;
  currency: string | null;
  interval: BillingInterval | null;
}

export interface PagedResult<T> {
  pageIndex: number;
  pageSize: number;
  count: number;
  data: T[];
}

export interface ProgramCard {
  id: string;
  title: string;
  summary: string | null;
  trainerId: string;
  kind: ProgramKind;
  startsOn: string | null;
  endsOn: string | null;
  price: ProgramPrice;
  coverUrl: string | null;
  weekCount: number;
}

export interface Media {
  id: string;
  kind: 'Image' | 'Video';
  contentType: string;
  caption: string | null;
  url: string;
}

export interface WeekOutline {
  id: string;
  number: number;
  title: string;
  releasesOn: string;
  isReleased: boolean;
}

export interface ProgramOverview {
  id: string;
  trainerId: string;
  title: string;
  summary: string | null;
  description: string | null;
  kind: ProgramKind;
  startsOn: string | null;
  endsOn: string | null;
  price: ProgramPrice;
  coverUrl: string | null;
  media: Media[];
  weeks: WeekOutline[];
}

export interface ExerciseSet {
  reps: number | null;
  weightKg: number | null;
  durationSec: number | null;
  distanceM: number | null;
  restSec: number | null;
  rpe: number | null;
}

export interface Exercise {
  id: string;
  order: number;
  name: string;
  notes: string | null;
  sets: ExerciseSet[];
  media: Media[];
}

export interface Workout {
  id: string;
  order: number;
  title: string;
  notes: string | null;
  exercises: Exercise[];
  media: Media[];
}

export interface Day {
  id: string;
  number: number;
  title: string | null;
  isRestDay: boolean;
  notes: string | null;
  workouts: Workout[];
  media: Media[];
}

export interface Week extends WeekOutline {
  days: Day[];
  media: Media[];
}

export interface ProgramContent {
  id: string;
  trainerId: string;
  title: string;
  summary: string | null;
  description: string | null;
  kind: ProgramKind;
  status: ProgramStatus;
  startsOn: string | null;
  endsOn: string | null;
  price: ProgramPrice;
  coverUrl: string | null;
  media: Media[];
  weeks: Week[];
}

export interface MyProgramRow {
  id: string;
  title: string;
  kind: ProgramKind;
  status: ProgramStatus;
  weekCount: number;
  price: ProgramPrice;
  createdAt: string;
  publishedAt: string | null;
}

export interface ProgramDetailsRequest {
  title: string;
  summary: string | null;
  description: string | null;
  kind: ProgramKind;
  startsOn: string | null;
  endsOn: string | null;
}

export interface ExerciseRequest {
  id: string | null;
  name: string;
  notes: string | null;
  sets: ExerciseSet[];
}

export interface WorkoutRequest {
  title: string;
  notes: string | null;
  exercises: ExerciseRequest[];
}

export interface PresignedUpload {
  objectKey: string;
  uploadUrl: string;
  expiresAt: string;
}

export interface BrowseQuery {
  search?: string;
  free?: boolean | null;
  kind?: ProgramKind | null;
  pageIndex?: number;
  pageSize?: number;
}

export function formatPrice(price: ProgramPrice): string {
  if (price.model === 'Free' || price.amountMinor === null) {
    return 'Free';
  }

  const amount = new Intl.NumberFormat(undefined, { style: 'currency', currency: price.currency ?? 'USD' }).format(
    price.amountMinor / 100,
  );

  return price.model === 'Recurring' ? `${amount} / ${price.interval === 'Yearly' ? 'year' : 'month'}` : amount;
}

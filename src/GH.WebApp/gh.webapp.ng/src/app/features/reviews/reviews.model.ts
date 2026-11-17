/** Mirrors GymHub.Modules.Reviews DTOs. Enums are sent as strings. */
import { ReportReason, ReportStatus } from '../discussions/discussion.model';
import { PagedResult, ProgramRating } from '../programs/program.model';

export const maxStars = 5;
export const maxReviewLength = 2000;

export interface Reviewer {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
}

export interface ReviewReply {
  body: string;
  trainerName: string;
  repliedAt: string;
  updatedAt: string;
}

export interface Review {
  id: string;
  programId: string;
  author: Reviewer;
  stars: number;
  body: string | null;
  createdAt: string;
  updatedAt: string;
  /** Only ever true on your own review (hidden reviews aren't listed). */
  isHidden: boolean;
  reply: ReviewReply | null;
}

export interface RatingSummary {
  average: number | null;
  count: number;
  /** starCounts[0] is 1-star reviews, up to starCounts[4] for 5 stars. */
  starCounts: number[];
}

export interface ProgramReviews {
  summary: RatingSummary;
  reviews: PagedResult<Review>;
}

export interface MyReview {
  review: Review | null;
  canWrite: boolean;
  /** Why the caller can't write right now. */
  reason: string | null;
}

export interface WriteReviewRequest {
  stars: number;
  body: string | null;
}

export interface ReviewReport {
  id: string;
  reason: ReportReason;
  note: string | null;
  status: ReportStatus;
  createdAt: string;
  reporterId: string;
  programId: string;
  programTitle: string;
  review: Review;
}

/** "4.5 · 12 reviews", or "No reviews yet". */
export function formatRating(rating: ProgramRating): string {
  if (!rating.count || rating.average === null) {
    return 'No reviews yet';
  }
  return `${rating.average.toFixed(1)} · ${rating.count} ${rating.count === 1 ? 'review' : 'reviews'}`;
}

/** How full the stars are drawn, as a percentage of all five. */
export function starPercent(value: number | null): number {
  return value === null ? 0 : Math.min(Math.max(value / maxStars, 0), 1) * 100;
}

/** Bars for 5 stars down to 1, each as its share of all reviews. */
export function starBars(summary: RatingSummary): { stars: number; count: number; percent: number }[] {
  return [5, 4, 3, 2, 1].map((stars) => {
    const count = summary.starCounts[stars - 1] ?? 0;
    return { stars, count, percent: summary.count ? (count / summary.count) * 100 : 0 };
  });
}

export type ApplicationStatus = 'Pending' | 'Approved' | 'Rejected';

/** Body of an application and of a profile edit. */
export interface TrainerDetailsRequest {
  displayName: string;
  bio: string | null;
  specialties: string[];
  links: string[];
}

export interface TrainerApplication {
  id: string;
  userId: string;
  displayName: string;
  bio: string | null;
  specialties: string[];
  links: string[];
  status: ApplicationStatus;
  submittedAt: string;
  reviewedAt: string | null;
  rejectionReason: string | null;
}

export interface TrainerProfile {
  userId: string;
  displayName: string;
  bio: string | null;
  specialties: string[];
  links: string[];
  avatarUrl: string | null;
  followerCount: number;
  isFollowing: boolean;
}

export interface MyTrainerProfile {
  userId: string;
  displayName: string;
  bio: string | null;
  specialties: string[];
  links: string[];
  avatarUrl: string | null;
}

export interface FollowedTrainer {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  specialties: string[];
  followedAt: string;
}

/** Splits a comma- or newline-separated field into trimmed, non-empty entries. */
export function splitList(value: string): string[] {
  return value
    .split(/[,\n]/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

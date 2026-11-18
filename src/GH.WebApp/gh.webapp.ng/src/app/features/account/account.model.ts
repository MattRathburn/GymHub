export interface PendingDeletion {
  requestedAt: string;
  scheduledFor: string;
}

/** The caller's account: a pending deletion, if any, and what currently stops one. */
export interface AccountStatus {
  deletion: PendingDeletion | null;
  deletionBlockers: string[];
}

/** What the member types to confirm. The API checks the same phrase, ignoring case and outer spaces. */
export const deletionPhrase = 'delete my account';

export function confirmsDeletion(text: string): boolean {
  return text.trim().toLowerCase() === deletionPhrase;
}

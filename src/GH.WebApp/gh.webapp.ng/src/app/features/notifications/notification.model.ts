/** Mirrors GymHub.Modules.Notifications DTOs. Enums are sent as strings. */

export type NotificationType =
  | 'WeekReleased'
  | 'PostReplied'
  | 'AnswerMarked'
  | 'ThreadStarted'
  | 'TrainerPublished'
  | 'NewMember'
  | 'TrainerApproved'
  | 'PriceChangeScheduled'
  | 'TrialEnding'
  | 'PaymentFailed'
  | 'AccessEnded';

export type NotificationGroup = 'Training' | 'Discussions' | 'Billing' | 'Trainer';

export interface NotificationItem {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  /** An app path such as /programs/{id}. */
  link: string;
  createdAt: string;
  isRead: boolean;
}

export interface NotificationPreference {
  type: NotificationType;
  group: NotificationGroup;
  label: string;
  inApp: boolean;
  email: boolean;
  emailLocked: boolean;
}

export interface PreferenceChange {
  type: NotificationType;
  inApp: boolean;
  email: boolean;
}

export const groupOrder: NotificationGroup[] = ['Training', 'Discussions', 'Billing', 'Trainer'];

/** Preferences grouped for the settings page, in a fixed group order. */
export function groupPreferences(preferences: NotificationPreference[]): { group: NotificationGroup; items: NotificationPreference[] }[] {
  return groupOrder
    .map((group) => ({ group, items: preferences.filter((p) => p.group === group) }))
    .filter((g) => g.items.length > 0);
}

/** The changes to save: every type the user moved away from what the server sent. Locked emails stay on. */
export function preferenceChanges(original: NotificationPreference[], edited: NotificationPreference[]): PreferenceChange[] {
  return edited
    .filter((p) => {
      const before = original.find((o) => o.type === p.type);
      return !before || before.inApp !== p.inApp || before.email !== p.email;
    })
    .map((p) => ({ type: p.type, inApp: p.inApp, email: p.emailLocked ? true : p.email }));
}

/** "3m", "5h", "2d" or a date, for compact lists. */
export function timeAgo(iso: string, now: Date = new Date()): string {
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60000);

  if (minutes < 1) {
    return 'now';
  }
  if (minutes < 60) {
    return `${minutes}m`;
  }
  if (minutes < 60 * 24) {
    return `${Math.floor(minutes / 60)}h`;
  }
  if (minutes < 60 * 24 * 7) {
    return `${Math.floor(minutes / (60 * 24))}d`;
  }

  return new Date(iso).toLocaleDateString();
}

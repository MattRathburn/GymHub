import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Placeholder privacy policy that describes what the app actually stores. Final text before launch (Milestone 8). */
@Component({
  selector: 'app-privacy',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="mx-auto max-w-3xl space-y-4 px-4 py-8 text-slate-700">
      <p class="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
        Draft: placeholder text, to be replaced with the final privacy policy before launch.
      </p>
      <h1 class="text-2xl font-semibold text-slate-900">Privacy policy</h1>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">What we store</h2>
      <ul class="list-disc space-y-1 pl-5">
        <li>Your sign-in: username, email address and password (held by our identity provider, never by GymHub itself).</li>
        <li>Your profile: display name and avatar; for trainers, bio, specialties and links.</li>
        <li>The programs you join, the trainers you follow, and your purchases. Card details stay with Stripe.</li>
        <li>Your training log, discussion posts and reviews.</li>
        <li>Your notifications and notification settings, and the email address we send them to.</li>
      </ul>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Who sees it</h2>
      <p>Your training log is private. Your display name and avatar appear next to your posts and reviews. A program's trainer sees who subscribes to it.</p>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Your choices</h2>
      <p>
        From your <a routerLink="/me/account" class="text-indigo-600 underline">account page</a> you can download everything we store about you, or
        delete your account. Deletion happens after a 7-day grace period: your profile, training log, enrollments,
        notifications and sign-in are removed. Posts and reviews stay, shown as by "Deleted user", and purchase records
        are kept for accounting without your profile.
      </p>
    </article>
  `,
})
export class PrivacyComponent {}

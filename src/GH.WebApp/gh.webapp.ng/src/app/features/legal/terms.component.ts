import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Placeholder terms. The final text is written before launch (roadmap Milestone 8). */
@Component({
  selector: 'app-terms',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="mx-auto max-w-3xl space-y-4 px-4 py-8 text-slate-700">
      <p class="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
        Draft: placeholder text, to be replaced with the final terms before launch.
      </p>
      <h1 class="text-2xl font-semibold text-slate-900">Terms of service</h1>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Your account</h2>
      <p>You need an account to join programs, post in discussions, log workouts and write reviews. Keep your sign-in to yourself; you're responsible for what happens under your account.</p>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Programs and trainers</h2>
      <p>Programs are written by independent trainers approved by GymHub. Training carries a risk of injury: check with a doctor before starting a new program, and stop if something hurts.</p>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Payments</h2>
      <p>Paid programs are sold by their trainer through GymHub and paid with Stripe. Subscriptions renew until you cancel them in Manage billing. Trainers can refund their own sales within 30 days.</p>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">What you post</h2>
      <p>Be respectful in discussions and reviews. Trainers and admins can hide content that breaks these rules, and anyone can report a post or review.</p>

      <h2 class="pt-2 text-lg font-semibold text-slate-900">Leaving</h2>
      <p>You can delete your account from your <a routerLink="/me/account" class="text-indigo-600 underline">account page</a>. The <a routerLink="/privacy" class="text-indigo-600 underline">privacy policy</a> explains what happens to your data.</p>
    </article>
  `,
})
export class TermsComponent {}

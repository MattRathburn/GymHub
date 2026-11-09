import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { MyEnrollment } from './subscription.model';
import { SubscriptionsService } from './subscriptions.service';

@Component({
  selector: 'app-my-subscriptions',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <h1 class="text-2xl font-semibold">My programs</h1>

      @switch (enrollments().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your programs.</p>
        }
      }

      @if (enrollments(); as state) {
        @if (state.status === 'ok') {
          <ul class="mt-6 space-y-3">
            @for (enrollment of state.value; track enrollment.programId) {
              <li>
                <a [routerLink]="['/programs', enrollment.programId]" class="card flex justify-between hover:shadow-md">
                  <span class="font-medium">{{ enrollment.programTitle ?? 'Program' }}</span>
                  @if (enrollment.programStatus === 'Archived') {
                    <span class="text-xs text-slate-500">Archived by trainer</span>
                  }
                </a>
              </li>
            } @empty {
              <li class="text-slate-500">
                You haven't joined a program yet. <a routerLink="/" class="text-indigo-600 underline">Browse programs</a>
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class MySubscriptionsComponent {
  private readonly subscriptions = inject(SubscriptionsService);

  protected readonly enrollments = toSignal(this.subscriptions.mine().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<MyEnrollment[]>,
  });
}

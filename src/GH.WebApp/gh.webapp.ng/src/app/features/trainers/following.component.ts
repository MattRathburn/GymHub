import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { FollowedTrainer } from './trainer.model';
import { TrainersService } from './trainers.service';

@Component({
  selector: 'app-following',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Trainers you follow</h1>

      @switch (following().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load who you follow.</p>
        }
      }

      @if (following(); as state) {
        @if (state.status === 'ok') {
          <ul class="mt-6 space-y-3">
            @for (trainer of state.value; track trainer.userId) {
              <li>
                <a [routerLink]="['/trainers', trainer.userId]" class="card flex items-center gap-4 hover:shadow-md">
                  @if (trainer.avatarUrl) {
                    <img [src]="trainer.avatarUrl" alt="" class="h-12 w-12 rounded-full object-cover" />
                  } @else {
                    <div class="flex h-12 w-12 items-center justify-center rounded-full bg-indigo-100 font-semibold text-indigo-700">
                      {{ trainer.displayName.charAt(0) }}
                    </div>
                  }
                  <div>
                    <p class="font-medium">{{ trainer.displayName }}</p>
                    @if (trainer.specialties.length) {
                      <p class="text-sm text-slate-500">{{ trainer.specialties.join(' · ') }}</p>
                    }
                  </div>
                </a>
              </li>
            } @empty {
              <li class="text-slate-500">
                You aren't following anyone yet. Follow trainers from their profile, linked on every program.
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class FollowingComponent {
  private readonly trainers = inject(TrainersService);

  protected readonly following = toSignal(this.trainers.following().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<FollowedTrainer[]>,
  });
}

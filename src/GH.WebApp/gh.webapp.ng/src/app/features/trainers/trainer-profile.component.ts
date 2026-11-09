import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { PagedResult, ProgramCard, formatPrice } from '../programs/program.model';
import { ProgramsService } from '../programs/programs.service';
import { TrainerProfile } from './trainer.model';
import { TrainersService } from './trainers.service';

/** A trainer's public page: who they are, their published programs, and a follow button. */
@Component({
  selector: 'app-trainer-profile',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      @switch (profile().status) {
        @case ('loading') {
          <p class="text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="text-rose-600">This trainer isn't available.</p>
        }
      }

      @if (trainer(); as t) {
        <div class="flex flex-wrap items-start gap-6">
          @if (t.avatarUrl) {
            <img [src]="t.avatarUrl" alt="" class="h-28 w-28 rounded-full object-cover" />
          } @else {
            <div class="flex h-28 w-28 items-center justify-center rounded-full bg-indigo-100 text-3xl font-semibold text-indigo-700">
              {{ t.displayName.charAt(0) }}
            </div>
          }

          <div class="min-w-0 flex-1">
            <h1 class="text-3xl font-semibold">{{ t.displayName }}</h1>
            <p class="mt-1 text-sm text-slate-500">{{ t.followerCount }} {{ t.followerCount === 1 ? 'follower' : 'followers' }}</p>
            @if (t.specialties.length) {
              <ul class="mt-3 flex flex-wrap gap-2">
                @for (specialty of t.specialties; track specialty) {
                  <li class="rounded bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">{{ specialty }}</li>
                }
              </ul>
            }
          </div>

          @if (isSelf()) {
            <a routerLink="/studio/profile" class="btn-secondary">Edit profile</a>
          } @else if (auth.isAuthenticated()) {
            <button [class]="t.isFollowing ? 'btn-secondary' : 'btn-primary'" [disabled]="busy()" (click)="toggleFollow(t)">
              {{ t.isFollowing ? 'Following' : 'Follow' }}
            </button>
          } @else {
            <button class="btn-primary" (click)="auth.login()">Sign in to follow</button>
          }
        </div>

        @if (actionError(); as message) {
          <p class="mt-2 text-sm text-rose-600">{{ message }}</p>
        }

        @if (t.bio) {
          <p class="mt-6 whitespace-pre-line text-slate-700">{{ t.bio }}</p>
        }
        @for (link of t.links; track link) {
          <a [href]="link" target="_blank" rel="noopener noreferrer" class="mt-1 block text-sm text-indigo-600 underline">{{ link }}</a>
        }

        <h2 class="mt-10 text-xl font-semibold">Programs</h2>
        @if (programs(); as state) {
          @if (state.status === 'ok') {
            <ul class="mt-4 grid gap-4 sm:grid-cols-2">
              @for (card of state.value.data; track card.id) {
                <li>
                  <a [routerLink]="['/programs', card.id]" class="card block h-full transition hover:shadow-md">
                    <div class="flex items-start justify-between gap-2">
                      <h3 class="font-semibold">{{ card.title }}</h3>
                      <span class="shrink-0 rounded bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">{{ formatPrice(card.price) }}</span>
                    </div>
                    @if (card.summary) {
                      <p class="mt-1 text-sm text-slate-600">{{ card.summary }}</p>
                    }
                  </a>
                </li>
              } @empty {
                <li class="text-slate-500">No published programs yet.</li>
              }
            </ul>
          }
        }
      }
    </section>
  `,
})
export class TrainerProfileComponent {
  private readonly trainers = inject(TrainersService);
  private readonly programsService = inject(ProgramsService);
  protected readonly auth = inject(AuthService);

  /** Route parameter: the trainer's user id. */
  readonly id = input.required<string>();

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly formatPrice = formatPrice;

  protected readonly profile = toSignal(
    toObservable(computed(() => ({ id: this.id(), refresh: this.refresh() }))).pipe(
      switchMap(({ id }) => this.trainers.profile(id).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<TrainerProfile> },
  );

  protected readonly programs = toSignal(
    toObservable(this.id).pipe(
      switchMap((trainerId) => this.programsService.browse({ trainerId, pageSize: 50 }).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<ProgramCard>> },
  );

  protected readonly trainer = computed(() => {
    const state = this.profile();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly isSelf = computed(() => this.auth.user()?.subject === this.id());

  protected async toggleFollow(trainer: TrainerProfile): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      const action = trainer.isFollowing ? this.trainers.unfollow(trainer.userId) : this.trainers.follow(trainer.userId);
      await firstValueFrom(action, { defaultValue: undefined });
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.actionError.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

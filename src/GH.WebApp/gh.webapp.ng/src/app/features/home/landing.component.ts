import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ProgramCardComponent } from '../programs/program-card.component';
import { ProgramCard } from '../programs/program.model';
import { ProgramsService } from '../programs/programs.service';
import { TrainerCardComponent } from '../trainers/trainer-card.component';
import { TrainerCard } from '../trainers/trainer.model';
import { TrainersService } from '../trainers/trainers.service';

/** The front page: what GymHub is, a few top-rated and new programs, and some trainers. Sections hide when empty. */
@Component({
  selector: 'app-landing',
  imports: [RouterLink, ProgramCardComponent, TrainerCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="border-b border-slate-200 bg-gradient-to-b from-indigo-50 to-white">
      <div class="mx-auto max-w-6xl px-4 py-16">
        <h1 class="max-w-2xl text-4xl font-bold tracking-tight text-slate-900">Train with programs from coaches who know their stuff.</h1>
        <p class="mt-4 max-w-2xl text-lg text-slate-600">
          Pick a program, get a new week of workouts on schedule, log what you lift, and ask your coach when you're stuck.
        </p>
        <div class="mt-8 flex flex-wrap gap-3">
          <a routerLink="/programs" class="btn-primary">Browse programs</a>
          <a routerLink="/trainers" class="btn-secondary">Meet the trainers</a>
          @if (!auth.isAuthenticated()) {
            <button class="btn-secondary" (click)="auth.login()">Sign in</button>
          }
        </div>
      </div>
    </section>

    <div class="mx-auto max-w-6xl space-y-14 px-4 py-12">
      @if (topRated().length) {
        <section>
          <div class="flex items-baseline justify-between">
            <h2 class="text-xl font-semibold">Top rated</h2>
            <a routerLink="/programs" [queryParams]="{ sort: 'TopRated' }" class="text-sm text-indigo-600 hover:underline">See all →</a>
          </div>
          <ul class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (card of topRated(); track card.id) {
              <li><app-program-card [program]="card" /></li>
            }
          </ul>
        </section>
      }

      @if (newest().length) {
        <section>
          <div class="flex items-baseline justify-between">
            <h2 class="text-xl font-semibold">New programs</h2>
            <a routerLink="/programs" class="text-sm text-indigo-600 hover:underline">See all →</a>
          </div>
          <ul class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (card of newest(); track card.id) {
              <li><app-program-card [program]="card" /></li>
            }
          </ul>
        </section>
      }

      @if (trainers().length) {
        <section>
          <div class="flex items-baseline justify-between">
            <h2 class="text-xl font-semibold">Trainers</h2>
            <a routerLink="/trainers" class="text-sm text-indigo-600 hover:underline">All trainers →</a>
          </div>
          <ul class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (trainer of trainers(); track trainer.userId) {
              <li><app-trainer-card [trainer]="trainer" /></li>
            }
          </ul>
        </section>
      }

      <section>
        <h2 class="text-xl font-semibold">How it works</h2>
        <ol class="mt-4 grid gap-4 sm:grid-cols-3">
          @for (step of steps; track step.title; let i = $index) {
            <li class="card">
              <span class="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-600 text-sm font-semibold text-white">{{ i + 1 }}</span>
              <h3 class="mt-3 font-semibold">{{ step.title }}</h3>
              <p class="mt-1 text-sm text-slate-600">{{ step.text }}</p>
            </li>
          }
        </ol>
        @if (auth.isAuthenticated() && !auth.isTrainer()) {
          <p class="mt-6 text-sm text-slate-600">
            Coach people yourself? <a routerLink="/trainers/apply" class="text-indigo-600 underline">Apply to become a trainer</a>.
          </p>
        }
      </section>
    </div>
  `,
})
export class LandingComponent {
  private readonly programs = inject(ProgramsService);
  private readonly trainersService = inject(TrainersService);
  protected readonly auth = inject(AuthService);

  protected readonly steps = [
    { title: 'Find a program', text: 'Search by goal, trainer or price, and read what other members say in the reviews.' },
    { title: 'Train week by week', text: 'New weeks release on schedule. Log each workout and watch your lifts and records climb.' },
    { title: 'Ask your coach', text: 'Every program has a members-only board where the trainer answers questions.' },
  ];

  private readonly rated = toSignal(
    this.programs.browse({ sort: 'TopRated', pageSize: 3 }).pipe(
      map((page) => page.data),
      catchError(() => of([] as ProgramCard[])),
    ),
    { initialValue: [] as ProgramCard[] },
  );

  /** Top-rated programs that actually have reviews. */
  protected readonly topRated = computed(() => this.rated().filter((p) => p.rating.count > 0));

  protected readonly newest = toSignal(
    this.programs.browse({ pageSize: 3 }).pipe(
      map((page) => page.data),
      catchError(() => of([] as ProgramCard[])),
    ),
    { initialValue: [] as ProgramCard[] },
  );

  protected readonly trainers = toSignal(
    this.trainersService.directory('', 0, 3).pipe(
      map((page) => page.data),
      catchError(() => of([] as TrainerCard[])),
    ),
    { initialValue: [] as TrainerCard[] },
  );
}

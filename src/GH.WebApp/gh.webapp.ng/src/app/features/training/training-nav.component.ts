import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Header shared by the training log pages. */
@Component({
  selector: 'app-training-nav',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">Training log</h1>
      <nav class="flex items-center gap-4 text-sm">
        <a routerLink="/me/training" [routerLinkActiveOptions]="{ exact: true }" routerLinkActive="font-semibold text-indigo-700" class="text-slate-600 hover:text-slate-900">History</a>
        <a routerLink="/me/training/exercises" routerLinkActive="font-semibold text-indigo-700" class="text-slate-600 hover:text-slate-900">Exercises</a>
        <a routerLink="/me/training/log" class="btn-primary">Log workout</a>
      </nav>
    </div>
  `,
})
export class TrainingNavComponent {}

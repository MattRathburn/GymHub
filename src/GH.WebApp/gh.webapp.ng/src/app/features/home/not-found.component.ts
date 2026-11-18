import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-xl px-4 py-24 text-center">
      <p class="text-sm font-semibold text-indigo-600">404</p>
      <h1 class="mt-2 text-2xl font-semibold">Page not found</h1>
      <p class="mt-2 text-slate-600">That page doesn't exist, or it has moved.</p>
      <div class="mt-6 flex justify-center gap-3">
        <a routerLink="/" class="btn-primary">Go home</a>
        <a routerLink="/programs" class="btn-secondary">Browse programs</a>
      </div>
    </section>
  `,
})
export class NotFoundComponent {}

import { ChangeDetectionStrategy, Component, effect, inject, input } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, switchMap } from 'rxjs';
import { DiscussionsRealtimeService } from './discussions-realtime.service';
import { PostNodeComponent } from './post-node.component';
import { ThreadPageStore } from './thread-page.store';

/** One thread with its nested replies, moderation tools and live updates. */
@Component({
  selector: 'app-thread',
  imports: [RouterLink, PostNodeComponent],
  providers: [ThreadPageStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      <a [routerLink]="['/programs', id(), 'discussion']" class="text-sm text-indigo-600 hover:underline">← Back to the board</a>

      @switch (store.state().status) {
        @case ('loading') {
          <p class="mt-4 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-4 text-rose-600">This thread isn't available. It may have been removed.</p>
        }
      }

      @if (store.thread(); as thread) {
        <div class="mt-2 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 class="text-2xl font-semibold">{{ thread.title }}</h1>
            <div class="mt-1 flex flex-wrap gap-2 text-xs">
              <span class="rounded bg-slate-100 px-1.5 py-0.5">{{ thread.kind }}</span>
              @if (thread.isPinned) { <span class="rounded bg-amber-100 px-1.5 py-0.5 text-amber-800">Pinned</span> }
              @if (thread.isLocked) { <span class="rounded bg-slate-200 px-1.5 py-0.5">Locked</span> }
              @if (thread.isHidden) { <span class="rounded bg-rose-100 px-1.5 py-0.5 text-rose-800">Hidden from members</span> }
              <span class="flex items-center gap-1 text-slate-500">
                <span class="inline-block h-2 w-2 rounded-full" [class]="realtime.status() === 'live' ? 'bg-emerald-500' : 'bg-slate-300'"></span>
                {{ realtime.status() === 'live' ? 'Live' : 'Offline' }}
              </span>
            </div>
          </div>

          @if (thread.canModerate) {
            <div class="flex flex-wrap gap-2">
              <button class="btn-secondary" [disabled]="store.busy()" (click)="store.moderate({ pinned: !thread.isPinned })">{{ thread.isPinned ? 'Unpin' : 'Pin' }}</button>
              <button class="btn-secondary" [disabled]="store.busy()" (click)="store.moderate({ locked: !thread.isLocked })">{{ thread.isLocked ? 'Unlock' : 'Lock' }}</button>
              <button class="btn-danger" [disabled]="store.busy()" (click)="store.moderate({ hidden: !thread.isHidden })">{{ thread.isHidden ? 'Unhide thread' : 'Hide thread' }}</button>
            </div>
          }
        </div>

        @if (thread.isLocked) {
          <p class="mt-3 text-sm text-slate-500">This thread is locked{{ thread.canModerate ? '; only moderators can reply.' : '.' }}</p>
        } @else if (!thread.canReply) {
          <p class="mt-3 text-sm text-slate-500">This board is read-only.</p>
        }

        @if (store.notice(); as message) {
          <p class="mt-3 text-sm text-emerald-700">{{ message }}</p>
        }
        @if (store.error(); as message) {
          <p class="mt-3 text-sm text-rose-600">{{ message }}</p>
        }

        <div class="card mt-4">
          @for (root of store.tree(); track root.post.id) {
            <app-post-node [node]="root" />
          }
        </div>

        @if (thread.hasMorePosts) {
          <p class="mt-3 text-sm text-slate-500">This thread is very long; only the first 500 posts are shown.</p>
        }
      }
    </section>
  `,
})
export class ThreadComponent {
  protected readonly store = inject(ThreadPageStore);
  protected readonly realtime = inject(DiscussionsRealtimeService);
  private readonly router = inject(Router);

  /** Route parameters, bound by withComponentInputBinding. */
  readonly id = input.required<string>();
  readonly threadId = input.required<string>();

  private loadedOnce = false;

  constructor() {
    effect(() => this.store.threadId.set(this.threadId()));

    toObservable(this.threadId)
      .pipe(
        switchMap((threadId) => this.realtime.watchThread(threadId)),
        debounceTime(300),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.store.reload());

    // The thread disappeared while open (deleted by its author, or hidden): go back to the board.
    effect(() => {
      const state = this.store.state();
      if (state.status === 'ok') {
        this.loadedOnce = true;
      } else if (state.status === 'error' && state.code === 404 && this.loadedOnce) {
        void this.router.navigate(['/programs', this.id(), 'discussion']);
      }
    });
  }
}

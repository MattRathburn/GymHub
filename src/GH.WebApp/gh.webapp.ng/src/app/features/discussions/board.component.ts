import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, filter, firstValueFrom, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { TrainersService } from '../trainers/trainers.service';
import { Board, ThreadKind } from './discussion.model';
import { DiscussionsRealtimeService } from './discussions-realtime.service';
import { DiscussionsService } from './discussions.service';

/** A program's discussion board: the thread list, filters and the new-thread form. */
@Component({
  selector: 'app-board',
  imports: [RouterLink, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      @switch (board().status) {
        @case ('loading') {
          <p class="text-slate-500">Loading…</p>
        }
        @case ('error') {
          @if (errorCode() === 403) {
            <p class="text-slate-700">The discussion board is for this program's subscribers.</p>
            <a [routerLink]="['/programs', id()]" class="mt-2 inline-block text-indigo-600 hover:underline">See the program</a>
          } @else {
            <p class="text-rose-600">This board isn't available.</p>
          }
        }
      }

      @if (current(); as b) {
        <a [routerLink]="['/programs', id()]" class="text-sm text-indigo-600 hover:underline">← {{ b.programTitle }}</a>
        <div class="mt-2 flex flex-wrap items-center justify-between gap-3">
          <h1 class="text-2xl font-semibold">Discussion</h1>
          <div class="flex items-center gap-3">
            <span class="flex items-center gap-1 text-xs text-slate-500" [title]="'Live updates: ' + realtime.status()">
              <span class="inline-block h-2 w-2 rounded-full" [class]="realtime.status() === 'live' ? 'bg-emerald-500' : 'bg-slate-300'"></span>
              {{ realtime.status() === 'live' ? 'Live' : 'Offline' }}
            </span>
            @if (b.canPost) {
              <button class="btn-primary" (click)="composing.set(!composing())">{{ composing() ? 'Cancel' : 'New thread' }}</button>
            } @else {
              <span class="text-sm text-slate-500">This program was archived; the board is read-only.</span>
            }
          </div>
        </div>

        @if (needsProfile()) {
          <div class="card mt-4 flex flex-wrap items-center gap-3 border-indigo-200 bg-indigo-50">
            <p class="text-sm text-slate-700">Choose the name others see next to your posts.</p>
            <input #name class="input max-w-xs" maxlength="40" [value]="auth.user()?.name ?? ''" />
            <button class="btn-secondary" [disabled]="busy()" (click)="saveName(name.value)">Save name</button>
          </div>
        }

        @if (composing()) {
          <form class="card mt-4 space-y-3" (submit)="$event.preventDefault(); start(kind.value, title.value, body.value)">
            <div class="flex flex-wrap gap-3">
              <select #kind class="input w-40">
                <option value="Question">Question</option>
                <option value="General">General</option>
              </select>
              <input #title class="input flex-1" maxlength="150" placeholder="Title" required />
            </div>
            <textarea #body class="input min-h-28" maxlength="5000" placeholder="What's on your mind?" required></textarea>
            <button class="btn-primary" type="submit" [disabled]="busy()">Post thread</button>
          </form>
        }

        <div class="mt-6 flex flex-wrap items-center gap-2">
          @for (option of kindOptions; track option.label) {
            <button class="rounded-full px-3 py-1 text-sm"
              [class]="kindFilter() === option.value ? 'bg-indigo-600 text-white' : 'bg-slate-100 text-slate-700 hover:bg-slate-200'"
              (click)="kindFilter.set(option.value); page.set(0)">{{ option.label }}</button>
          }
          <form class="ml-auto flex gap-2" (submit)="$event.preventDefault(); search.set(q.value); page.set(0)">
            <input #q class="input w-56" placeholder="Search titles" [value]="search()" />
            <button class="btn-secondary" type="submit">Search</button>
          </form>
        </div>

        @if (error(); as message) {
          <p class="mt-4 text-sm text-rose-600">{{ message }}</p>
        }

        <ul class="mt-4 divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white">
          @for (thread of b.threads.data; track thread.id) {
            <li class="px-4 py-3" [class.opacity-60]="thread.isHidden">
              <a [routerLink]="['/programs', id(), 'discussion', thread.id]" class="font-medium text-slate-900 hover:text-indigo-700">{{ thread.title }}</a>
              <div class="mt-1 flex flex-wrap items-center gap-2 text-xs text-slate-500">
                @if (thread.isPinned) { <span class="rounded bg-amber-100 px-1.5 py-0.5 text-amber-800">Pinned</span> }
                @if (thread.kind === 'Question') {
                  <span class="rounded px-1.5 py-0.5" [class]="thread.isAnswered ? 'bg-emerald-100 text-emerald-800' : 'bg-sky-100 text-sky-800'">
                    {{ thread.isAnswered ? 'Answered' : 'Question' }}
                  </span>
                }
                @if (thread.isLocked) { <span class="rounded bg-slate-200 px-1.5 py-0.5">Locked</span> }
                @if (thread.isHidden) { <span class="rounded bg-rose-100 px-1.5 py-0.5 text-rose-800">Hidden</span> }
                <span>{{ thread.author.displayName }}@if (thread.author.isTrainer) { (trainer)}</span>
                <span>· {{ thread.replyCount }} {{ thread.replyCount === 1 ? 'reply' : 'replies' }}</span>
                <span>· active {{ thread.lastActivityAt | date: 'medium' }}</span>
              </div>
            </li>
          } @empty {
            <li class="px-4 py-6 text-center text-sm text-slate-500">No threads yet. Start the conversation!</li>
          }
        </ul>

        @if (b.threads.count > b.threads.pageSize) {
          <div class="mt-4 flex items-center justify-center gap-3 text-sm">
            <button class="btn-secondary" [disabled]="page() === 0" (click)="page.set(page() - 1)">Newer</button>
            <span>Page {{ page() + 1 }} of {{ pageCount(b) }}</span>
            <button class="btn-secondary" [disabled]="page() + 1 >= pageCount(b)" (click)="page.set(page() + 1)">Older</button>
          </div>
        }
      }
    </section>
  `,
})
export class BoardComponent {
  private readonly discussions = inject(DiscussionsService);
  private readonly trainers = inject(TrainersService);
  private readonly router = inject(Router);
  protected readonly realtime = inject(DiscussionsRealtimeService);
  protected readonly auth = inject(AuthService);

  /** Program id route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly kindOptions: { label: string; value: ThreadKind | undefined }[] = [
    { label: 'All', value: undefined },
    { label: 'Questions', value: 'Question' },
    { label: 'General', value: 'General' },
  ];

  protected readonly kindFilter = signal<ThreadKind | undefined>(undefined);
  protected readonly search = signal('');
  protected readonly page = signal(0);
  protected readonly composing = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly hasProfile = signal<boolean | null>(null);
  /** silent refreshes (live updates, after posting) keep the current list on screen while reloading. */
  private readonly refresh = signal({ count: 0, silent: false });

  protected readonly board = toSignal(
    toObservable(
      computed(() => ({ id: this.id(), kind: this.kindFilter(), q: this.search(), pageIndex: this.page(), refresh: this.refresh() })),
    ).pipe(
      switchMap((query) =>
        this.discussions.board(query.id, query).pipe(
          toLoadState(),
          filter((state) => !(query.refresh.silent && state.status === 'loading')),
        ),
      ),
    ),
    { initialValue: { status: 'loading' } as LoadState<Board> },
  );

  protected readonly current = computed(() => {
    const state = this.board();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly errorCode = computed(() => {
    const state = this.board();
    return state.status === 'error' ? state.code : null;
  });

  protected readonly needsProfile = computed(() => this.hasProfile() === false && !this.auth.isTrainer() && !!this.current()?.canPost);

  constructor() {
    toObservable(this.id)
      .pipe(
        switchMap((id) => this.realtime.watchBoard(id)),
        debounceTime(300),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.reload());

    this.trainers
      .myMemberProfile()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: () => this.hasProfile.set(true),
        error: (error: unknown) => this.hasProfile.set(error instanceof HttpErrorResponse && error.status === 404 ? false : null),
      });
  }

  protected pageCount(board: Board): number {
    return Math.max(1, Math.ceil(board.threads.count / board.threads.pageSize));
  }

  protected async start(kind: string, title: string, body: string): Promise<void> {
    await this.run(async () => {
      const started = await firstValueFrom(
        this.discussions.startThread(this.id(), { kind: kind as ThreadKind, title: title.trim(), body: body.trim() }),
      );
      this.composing.set(false);
      await this.router.navigate(['/programs', this.id(), 'discussion', started.id]);
    });
  }

  protected async saveName(name: string): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.trainers.saveMyMemberProfile(name.trim()));
      this.hasProfile.set(true);
      this.reload();
    });
  }

  private reload(): void {
    this.refresh.update((r) => ({ count: r.count + 1, silent: true }));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

import { Injectable, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, filter, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { ModerateThreadRequest, ReportReason, Thread, buildTree } from './discussion.model';
import { DiscussionsService } from './discussions.service';

/**
 * State and actions for one thread page, shared by the page and every (recursive) post node.
 * Provided by ThreadComponent, so each page gets its own.
 */
@Injectable()
export class ThreadPageStore {
  private readonly discussions = inject(DiscussionsService);

  readonly threadId = signal<string | null>(null);
  private readonly refresh = signal({ count: 0, silent: false });

  /** Which post has an open reply, edit or report form; one at a time. */
  readonly replyingTo = signal<string | null>(null);
  readonly editing = signal<string | null>(null);
  readonly reporting = signal<string | null>(null);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  readonly state = toSignal(
    toObservable(computed(() => ({ id: this.threadId(), refresh: this.refresh() }))).pipe(
      filter((query): query is { id: string; refresh: { count: number; silent: boolean } } => query.id !== null),
      switchMap((query) =>
        this.discussions.thread(query.id).pipe(
          toLoadState(),
          filter((state) => !(query.refresh.silent && state.status === 'loading')),
        ),
      ),
    ),
    { initialValue: { status: 'loading' } as LoadState<Thread> },
  );

  readonly thread = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? state.value : null;
  });

  readonly tree = computed(() => buildTree(this.thread()?.posts ?? []));

  /** Reloads in the background, keeping the thread on screen. */
  reload(): void {
    this.refresh.update((r) => ({ count: r.count + 1, silent: true }));
  }

  openReply(postId: string): void {
    this.editing.set(null);
    this.reporting.set(null);
    this.replyingTo.set(this.replyingTo() === postId ? null : postId);
  }

  openEdit(postId: string): void {
    this.replyingTo.set(null);
    this.reporting.set(null);
    this.editing.set(this.editing() === postId ? null : postId);
  }

  openReport(postId: string): void {
    this.replyingTo.set(null);
    this.editing.set(null);
    this.reporting.set(this.reporting() === postId ? null : postId);
  }

  reply(parentPostId: string, body: string): Promise<boolean> {
    return this.run(this.discussions.reply(this.id(), parentPostId, body.trim()), () => this.replyingTo.set(null));
  }

  edit(postId: string, body: string): Promise<boolean> {
    return this.run(this.discussions.editPost(postId, body.trim()), () => this.editing.set(null));
  }

  delete(postId: string): Promise<boolean> {
    return this.run(this.discussions.deletePost(postId));
  }

  setHidden(postId: string, hidden: boolean): Promise<boolean> {
    return this.run(this.discussions.setPostHidden(postId, hidden));
  }

  markAnswer(postId: string | null): Promise<boolean> {
    return this.run(this.discussions.markAnswer(this.id(), postId));
  }

  moderate(request: ModerateThreadRequest): Promise<boolean> {
    return this.run(this.discussions.moderateThread(this.id(), request));
  }

  report(postId: string, reason: ReportReason, note: string): Promise<boolean> {
    return this.run(this.discussions.report(postId, reason, note.trim() || null), () => {
      this.reporting.set(null);
      this.notice.set('Thanks, an admin will review it.');
    });
  }

  private id(): string {
    return this.threadId()!;
  }

  private async run(action: Observable<unknown>, onSuccess?: () => void): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await firstValueFrom(action, { defaultValue: undefined });
      onSuccess?.();
      this.reload();
      return true;
    } catch (error) {
      this.error.set(problemMessage(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

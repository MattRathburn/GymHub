import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { PostNode, ReportReason, reportReasons } from './discussion.model';
import { ThreadPageStore } from './thread-page.store';

/** One post and, recursively, its replies. Text is rendered as plain text (interpolation escapes it). */
@Component({
  selector: 'app-post-node',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let post = node().post;
    @let thread = store.thread();
    <article [id]="'post-' + post.id" class="rounded-md" [class]="post.isAnswer ? 'border border-emerald-300 bg-emerald-50 p-3' : 'py-2'">
      <header class="flex flex-wrap items-center gap-2 text-sm">
        @if (post.author; as author) {
          @if (author.avatarUrl) {
            <img [src]="author.avatarUrl" alt="" class="h-6 w-6 rounded-full object-cover" />
          } @else {
            <span class="flex h-6 w-6 items-center justify-center rounded-full bg-slate-200 text-xs text-slate-600">{{ author.displayName.charAt(0).toUpperCase() }}</span>
          }
          <span class="font-medium text-slate-900">{{ author.displayName }}</span>
          @if (author.isTrainer) {
            <span class="rounded bg-indigo-100 px-1.5 py-0.5 text-xs font-medium text-indigo-800">Trainer</span>
          }
        } @else {
          <span class="text-slate-400">[deleted]</span>
        }
        <span class="text-xs text-slate-500">{{ post.createdAt | date: 'medium' }}@if (post.editedAt) { · edited}</span>
        @if (post.isAnswer) {
          <span class="rounded bg-emerald-600 px-1.5 py-0.5 text-xs font-medium text-white">Answer</span>
        }
        @if (post.isHidden) {
          <span class="rounded bg-rose-100 px-1.5 py-0.5 text-xs text-rose-800">Hidden by a moderator</span>
        }
      </header>

      @if (store.editing() === post.id) {
        <form class="mt-2 space-y-2" (submit)="$event.preventDefault(); store.edit(post.id, editBody.value)">
          <textarea #editBody class="input min-h-24" maxlength="5000" [value]="post.body ?? ''"></textarea>
          <div class="flex gap-2">
            <button class="btn-primary" type="submit" [disabled]="store.busy()">Save</button>
            <button class="btn-secondary" type="button" (click)="store.openEdit(post.id)">Cancel</button>
          </div>
        </form>
      } @else if (post.body !== null) {
        <p class="mt-1 whitespace-pre-line break-words text-slate-800" [class.text-lg]="isOpening()" [class.opacity-60]="post.isHidden">{{ post.body }}</p>
      } @else if (post.isDeleted) {
        <p class="mt-1 text-sm italic text-slate-400">This post was deleted.</p>
      } @else {
        <p class="mt-1 text-sm italic text-slate-400">This post was removed by a moderator.</p>
      }

      @if (thread && !post.isDeleted) {
        <div class="mt-1 flex flex-wrap gap-3 text-xs text-slate-500">
          @if (thread.canReply && !post.isHidden && post.depth < maxDepth) {
            <button class="hover:text-indigo-700" (click)="store.openReply(post.id)">Reply</button>
          }
          @if (post.isMine && !thread.isLocked && !post.isHidden) {
            <button class="hover:text-indigo-700" (click)="store.openEdit(post.id)">Edit</button>
          }
          @if (post.isMine) {
            <button class="hover:text-rose-700" [disabled]="store.busy()" (click)="confirmDelete()">Delete</button>
          }
          @if (thread.canMarkAnswer && !isOpening() && !post.isHidden) {
            <button class="hover:text-emerald-700" [disabled]="store.busy()" (click)="store.markAnswer(post.isAnswer ? null : post.id)">
              {{ post.isAnswer ? 'Unmark answer' : 'Mark as answer' }}
            </button>
          }
          @if (thread.canModerate) {
            <button class="hover:text-rose-700" [disabled]="store.busy()" (click)="store.setHidden(post.id, !post.isHidden)">
              {{ post.isHidden ? 'Unhide' : 'Hide' }}
            </button>
          }
          @if (!post.isMine && !post.isHidden) {
            <button class="hover:text-rose-700" (click)="store.openReport(post.id)">Report</button>
          }
        </div>
      }

      @if (store.reporting() === post.id) {
        <form class="mt-2 flex flex-wrap items-center gap-2" (submit)="$event.preventDefault(); store.report(post.id, $any(reason.value), note.value)">
          <select #reason class="input w-48">
            @for (option of reasons; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
          <input #note class="input flex-1" maxlength="500" placeholder="Anything an admin should know? (optional)" />
          <button class="btn-danger" type="submit" [disabled]="store.busy()">Report</button>
          <button class="btn-secondary" type="button" (click)="store.openReport(post.id)">Cancel</button>
        </form>
      }

      @if (store.replyingTo() === post.id) {
        <form class="mt-2 space-y-2" (submit)="$event.preventDefault(); sendReply(replyBody)">
          <textarea #replyBody class="input min-h-20" maxlength="5000" placeholder="Write a reply…"></textarea>
          <div class="flex gap-2">
            <button class="btn-primary" type="submit" [disabled]="store.busy()">Reply</button>
            <button class="btn-secondary" type="button" (click)="store.openReply(post.id)">Cancel</button>
          </div>
        </form>
      }
    </article>

    @if (node().children.length) {
      <div [class]="isOpening() ? 'mt-4 space-y-2' : 'ml-3 mt-1 space-y-1 border-l-2 border-slate-200 pl-3 sm:ml-4 sm:pl-4'">
        @for (child of node().children; track child.post.id) {
          <app-post-node [node]="child" />
        }
      </div>
    }
  `,
})
export class PostNodeComponent {
  protected readonly store = inject(ThreadPageStore);

  readonly node = input.required<PostNode>();

  /** Mirrors DiscussionThread.MaxDepth on the server. */
  protected readonly maxDepth = 8;
  protected readonly reasons: { value: ReportReason; label: string }[] = reportReasons;
  protected readonly isOpening = computed(() => this.node().post.parentPostId === null);

  protected async sendReply(textarea: HTMLTextAreaElement): Promise<void> {
    if (await this.store.reply(this.node().post.id, textarea.value)) {
      textarea.value = '';
    }
  }

  protected confirmDelete(): void {
    const message = this.isOpening() && this.node().children.length === 0 ? 'Delete this thread?' : 'Delete this post?';
    if (globalThis.confirm(message)) {
      void this.store.delete(this.node().post.id);
    }
  }
}

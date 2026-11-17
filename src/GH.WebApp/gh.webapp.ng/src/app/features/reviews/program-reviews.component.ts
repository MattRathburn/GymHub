import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Observable, catchError, firstValueFrom, of, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { ReportReason, reportReasons } from '../discussions/discussion.model';
import { MyReview, ProgramReviews, Review, maxReviewLength, maxStars, starBars } from './reviews.model';
import { ReviewsService } from './reviews.service';
import { StarRatingComponent } from './star-rating.component';

/**
 * A program's reviews on its page: the rating summary, your own review (when you have access), every visible review
 * with the trainer's reply, replying for the trainer, and reporting for everyone else.
 */
@Component({
  selector: 'app-program-reviews',
  imports: [DatePipe, StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section id="reviews" class="scroll-mt-20">
      <h2 class="text-xl font-semibold">Reviews</h2>

      @if (error(); as message) {
        <p class="mt-3 rounded bg-rose-50 px-3 py-2 text-sm text-rose-700">{{ message }}</p>
      }
      @if (notice(); as message) {
        <p class="mt-3 rounded bg-emerald-50 px-3 py-2 text-sm text-emerald-700">{{ message }}</p>
      }

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-3 text-slate-500">Loading reviews…</p>
        }
        @case ('error') {
          <p class="mt-3 text-rose-600">Couldn't load reviews.</p>
        }
      }

      @if (summary(); as s) {
        <div class="mt-4 flex flex-wrap items-center gap-8">
          <div class="text-center">
            <p class="text-4xl font-semibold">{{ s.average === null ? '–' : s.average.toFixed(1) }}</p>
            <app-star-rating [value]="s.average" size="lg" />
            <p class="text-sm text-slate-500">{{ s.count }} {{ s.count === 1 ? 'review' : 'reviews' }}</p>
          </div>
          <ul class="min-w-56 grow space-y-1 text-sm" aria-label="Reviews by stars">
            @for (bar of bars(); track bar.stars) {
              <li class="flex items-center gap-2">
                <span class="w-12 text-slate-600">{{ bar.stars }} star</span>
                <span class="h-2 grow overflow-hidden rounded-full bg-slate-100">
                  <span class="block h-full rounded-full bg-amber-400" [style.width.%]="bar.percent"></span>
                </span>
                <span class="w-8 text-right text-slate-500">{{ bar.count }}</span>
              </li>
            }
          </ul>
        </div>
      }

      @if (mine(); as m) {
        @if (m.canWrite || m.review) {
          <div class="card mt-6">
            <h3 class="font-semibold">{{ m.review ? 'Your review' : 'Review this program' }}</h3>
            @if (m.review?.isHidden) {
              <p class="mt-1 text-sm text-amber-700">An admin hid your review, so others can't see it and it doesn't count.</p>
            }

            @if (m.canWrite) {
              <form class="mt-3 space-y-3" (submit)="$event.preventDefault(); save()">
                <div class="flex items-center gap-1" role="radiogroup" aria-label="Stars">
                  @for (n of starChoices; track n) {
                    <button
                      type="button"
                      role="radio"
                      class="text-3xl leading-none"
                      [class.text-amber-400]="n <= draftStars()"
                      [class.text-slate-300]="n > draftStars()"
                      [attr.aria-checked]="n === draftStars()"
                      [attr.aria-label]="n + (n === 1 ? ' star' : ' stars')"
                      (click)="draftStars.set(n)"
                    >★</button>
                  }
                </div>
                <textarea
                  class="input"
                  rows="4"
                  [maxLength]="maxLength"
                  placeholder="What worked, what didn't? (optional)"
                  aria-label="Review text"
                  [value]="draftBody()"
                  (input)="draftBody.set($any($event.target).value)"
                ></textarea>
                <div class="flex gap-2">
                  <button class="btn-primary" type="submit" [disabled]="busy() || draftStars() === 0">
                    {{ m.review ? 'Update review' : 'Post review' }}
                  </button>
                  @if (m.review) {
                    <button class="btn-danger" type="button" [disabled]="busy()" (click)="remove()">Delete</button>
                  }
                </div>
              </form>
            } @else if (m.review; as r) {
              <div class="mt-2">
                <app-star-rating [value]="r.stars" />
                @if (r.body) {
                  <p class="mt-1 whitespace-pre-line text-sm text-slate-700">{{ r.body }}</p>
                }
                <p class="mt-2 text-xs text-slate-500">{{ m.reason }} You can still delete it.</p>
                <button class="btn-danger mt-2" type="button" [disabled]="busy()" (click)="remove()">Delete</button>
              </div>
            }
          </div>
        }
      }

      <ul class="mt-6 space-y-4">
        @for (review of reviews(); track review.id) {
          <li class="card">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <span class="flex items-center gap-2">
                @if (review.author.avatarUrl) {
                  <img [src]="review.author.avatarUrl" alt="" class="h-7 w-7 rounded-full object-cover" />
                }
                <span class="font-medium">{{ review.author.displayName }}</span>
                <app-star-rating [value]="review.stars" />
              </span>
              <span class="text-xs text-slate-500">
                {{ review.createdAt | date: 'mediumDate' }}@if (review.updatedAt !== review.createdAt) { · edited}
              </span>
            </div>
            @if (review.body) {
              <p class="mt-2 whitespace-pre-line text-sm text-slate-700">{{ review.body }}</p>
            }

            @if (review.reply; as reply) {
              <div class="mt-3 rounded border-l-4 border-indigo-200 bg-indigo-50/50 p-3 text-sm">
                <p class="text-xs font-semibold text-indigo-800">Reply from {{ reply.trainerName }}</p>
                <p class="mt-1 whitespace-pre-line text-slate-700">{{ reply.body }}</p>
              </div>
            }

            @if (replying() === review.id) {
              <form class="mt-3 space-y-2" (submit)="$event.preventDefault(); sendReply(review, replyText.value)">
                <textarea #replyText class="input" rows="3" [maxLength]="maxLength" aria-label="Your reply" [value]="review.reply?.body ?? ''"></textarea>
                <div class="flex gap-2">
                  <button class="btn-primary" type="submit" [disabled]="busy()">Save reply</button>
                  <button class="btn-secondary" type="button" (click)="replying.set(null)">Cancel</button>
                </div>
              </form>
            }

            @if (reporting() === review.id) {
              <form class="mt-3 flex flex-wrap items-center gap-2" (submit)="$event.preventDefault(); report(review, $any(reason.value), note.value)">
                <select #reason class="input w-48" aria-label="Reason">
                  @for (r of reasons; track r.value) {
                    <option [value]="r.value">{{ r.label }}</option>
                  }
                </select>
                <input #note class="input w-56 grow" maxlength="500" placeholder="Note for the admins (optional)" aria-label="Note" />
                <button class="btn-danger" type="submit" [disabled]="busy()">Report</button>
                <button class="btn-secondary" type="button" (click)="reporting.set(null)">Cancel</button>
              </form>
            }

            @if (auth.isAuthenticated() && replying() !== review.id && reporting() !== review.id) {
              <div class="mt-3 flex gap-4 text-xs text-slate-500">
                @if (isTrainer()) {
                  <button class="hover:text-indigo-700" (click)="replying.set(review.id)">{{ review.reply ? 'Edit reply' : 'Reply' }}</button>
                  @if (review.reply) {
                    <button class="hover:text-rose-700" [disabled]="busy()" (click)="removeReply(review)">Remove reply</button>
                  }
                }
                @if (review.author.userId !== userId()) {
                  <button class="hover:text-rose-700" (click)="reporting.set(review.id)">Report</button>
                }
              </div>
            }
          </li>
        } @empty {
          @if (page().status === 'ok') {
            <li class="text-slate-500">No reviews yet.</li>
          }
        }
      </ul>

      @if (hasMore()) {
        <button class="btn-secondary mt-4" [disabled]="loadingMore()" (click)="loadMore()">Load more reviews</button>
      }
    </section>
  `,
})
export class ProgramReviewsComponent {
  private readonly reviewsService = inject(ReviewsService);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly auth = inject(AuthService);

  readonly programId = input.required<string>();
  readonly trainerId = input.required<string>();
  /** Changes when the viewer's access changes (subscribe, leave), so "can I review?" is asked again. */
  readonly hasAccess = input(false);

  protected readonly starChoices = Array.from({ length: maxStars }, (_, i) => i + 1);
  protected readonly maxLength = maxReviewLength;
  protected readonly reasons = reportReasons;

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly replying = signal<string | null>(null);
  protected readonly reporting = signal<string | null>(null);
  protected readonly draftStars = signal(0);
  protected readonly draftBody = signal('');

  /** Reviews from "Load more", after the first page. */
  private readonly more = signal<Review[]>([]);
  protected readonly loadingMore = signal(false);

  protected readonly page = toSignal(
    toObservable(computed(() => ({ id: this.programId(), refresh: this.refresh() }))).pipe(
      switchMap(({ id }) => this.reviewsService.forProgram(id).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<ProgramReviews> },
  );

  protected readonly mine = toSignal(
    toObservable(
      computed(() => ({ id: this.programId(), signedIn: this.auth.isAuthenticated(), access: this.hasAccess(), refresh: this.refresh() })),
    ).pipe(
      switchMap(({ id, signedIn }) =>
        signedIn ? this.reviewsService.mine(id).pipe(catchError(() => of(null))) : of(null as MyReview | null),
      ),
    ),
    { initialValue: null },
  );

  protected readonly summary = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value.summary : null;
  });

  protected readonly bars = computed(() => {
    const summary = this.summary();
    return summary ? starBars(summary) : [];
  });

  protected readonly reviews = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? [...state.value.reviews.data, ...this.more()] : [];
  });

  protected readonly hasMore = computed(() => {
    const state = this.page();
    return state.status === 'ok' && this.reviews().length < state.value.reviews.count;
  });

  protected readonly userId = computed(() => this.auth.user()?.subject ?? '');
  protected readonly isTrainer = computed(() => this.userId() !== '' && this.userId() === this.trainerId());

  constructor() {
    // Start the form from the saved review each time it (re)loads.
    effect(() => {
      const review = this.mine()?.review;
      untracked(() => {
        this.draftStars.set(review?.stars ?? 0);
        this.draftBody.set(review?.body ?? '');
      });
    });

    // Links such as a new-review notification end in #reviews; scroll here once the reviews are in. The rest of the
    // program page may still be loading above, so scroll again once it has had a moment to settle.
    let scrolled = false;
    effect(() => {
      if (!scrolled && this.page().status === 'ok' && this.route.snapshot.fragment === 'reviews') {
        scrolled = true;
        const scroll = () => this.host.nativeElement.scrollIntoView();
        setTimeout(scroll);
        setTimeout(scroll, 600);
      }
    });
  }

  protected async loadMore(): Promise<void> {
    this.loadingMore.set(true);
    try {
      const pageIndex = Math.floor(this.reviews().length / 10);
      const next = await firstValueFrom(this.reviewsService.forProgram(this.programId(), pageIndex));
      const seen = new Set(this.reviews().map((r) => r.id));
      this.more.update((more) => [...more, ...next.reviews.data.filter((r) => !seen.has(r.id))]);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.loadingMore.set(false);
    }
  }

  protected save(): Promise<void> {
    const body = this.draftBody().trim();
    return this.run(this.reviewsService.write(this.programId(), { stars: this.draftStars(), body: body || null }), 'Review saved. Thanks!');
  }

  protected async remove(): Promise<void> {
    const locked = !this.mine()?.canWrite;
    const message = locked
      ? "Delete your review? Your access to this program has ended, so you won't be able to write it again."
      : 'Delete your review?';

    if (!confirm(message)) {
      return;
    }

    return this.run(this.reviewsService.remove(this.programId()), 'Review deleted.');
  }

  protected sendReply(review: Review, text: string): Promise<void> {
    return this.run(this.reviewsService.reply(review.id, text), 'Reply saved.');
  }

  protected removeReply(review: Review): Promise<void> {
    return this.run(this.reviewsService.removeReply(review.id), 'Reply removed.');
  }

  protected report(review: Review, reason: ReportReason, note: string): Promise<void> {
    return this.run(this.reviewsService.report(review.id, reason, note.trim() || null), 'Thanks. An admin will take a look.');
  }

  private async run(action: Observable<unknown>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await firstValueFrom(action, { defaultValue: undefined });
      this.notice.set(done);
      this.replying.set(null);
      this.reporting.set(null);
      this.more.set([]);
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

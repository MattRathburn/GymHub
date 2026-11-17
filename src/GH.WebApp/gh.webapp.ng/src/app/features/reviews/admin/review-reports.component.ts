import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { ReportStatus, reportReasons } from '../../discussions/discussion.model';
import { PagedResult } from '../../programs/program.model';
import { ReviewReport } from '../reviews.model';
import { ReviewsService } from '../reviews.service';
import { StarRatingComponent } from '../star-rating.component';

/** The "Reviews" tab of the admin Reports page: dismiss a report, or hide the review (closing its open reports). */
@Component({
  selector: 'app-review-reports',
  imports: [RouterLink, DatePipe, StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (notice(); as message) {
      <p class="mt-4 text-sm text-emerald-700">{{ message }}</p>
    }
    @if (error(); as message) {
      <p class="mt-4 text-sm text-rose-600">{{ message }}</p>
    }

    @switch (reports().status) {
      @case ('loading') {
        <p class="mt-6 text-slate-500">Loading…</p>
      }
      @case ('error') {
        <p class="mt-6 text-rose-600">Couldn't load reports.</p>
      }
    }

    @if (reports(); as state) {
      @if (state.status === 'ok') {
        <ul class="mt-6 space-y-4">
          @for (report of state.value.data; track report.id) {
            <li class="card">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <p class="font-semibold">{{ reasonLabel(report) }}</p>
                  <p class="text-xs text-slate-500">Reported {{ report.createdAt | date: 'medium' }} · by user {{ report.reporterId }}</p>
                </div>
                <a [routerLink]="['/programs', report.programId]" fragment="reviews" class="text-sm text-indigo-600 hover:underline">
                  {{ report.programTitle }}
                </a>
              </div>
              @if (report.note) {
                <p class="mt-2 text-sm text-slate-700">“{{ report.note }}”</p>
              }
              <blockquote class="mt-3 rounded border-l-4 border-slate-300 bg-slate-50 p-3 text-sm text-slate-800">
                <span class="mb-1 flex items-center gap-2 text-xs text-slate-500">
                  {{ report.review.author.displayName }} gave <app-star-rating [value]="report.review.stars" />
                  @if (report.review.isHidden) {
                    · now hidden
                  }
                </span>
                <span class="whitespace-pre-line">{{ report.review.body || '(no text)' }}</span>
              </blockquote>

              @if (report.status === 'Open') {
                <div class="mt-4 flex gap-2">
                  <button class="btn-danger" [disabled]="busy()" (click)="action(report)">Hide review</button>
                  <button class="btn-secondary" [disabled]="busy()" (click)="dismiss(report)">Dismiss</button>
                </div>
              } @else if (report.review.isHidden) {
                <div class="mt-4">
                  <button class="btn-secondary" [disabled]="busy()" (click)="show(report)">Show review again</button>
                </div>
              }
            </li>
          } @empty {
            <li class="text-slate-500">No {{ status().toLowerCase() }} review reports.</li>
          }
        </ul>
      }
    }
  `,
})
export class ReviewReportsComponent {
  private readonly reviews = inject(ReviewsService);

  readonly status = input.required<ReportStatus>();

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly reports = toSignal(
    toObservable(computed(() => ({ status: this.status(), refresh: this.refresh() }))).pipe(
      switchMap(({ status }) => this.reviews.reports(status).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<ReviewReport>> },
  );

  protected reasonLabel(report: ReviewReport): string {
    return reportReasons.find((r) => r.value === report.reason)?.label ?? report.reason;
  }

  protected action(report: ReviewReport): Promise<void> {
    return this.run(this.reviews.actionReport(report.id), 'Review hidden and its open reports closed.');
  }

  protected show(report: ReviewReport): Promise<void> {
    return this.run(this.reviews.showReview(report.review.id), 'Review shown again.');
  }

  protected dismiss(report: ReviewReport): Promise<void> {
    return this.run(this.reviews.dismissReport(report.id), 'Report dismissed.');
  }

  private async run(action: Observable<void>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await firstValueFrom(action, { defaultValue: undefined });
      this.notice.set(done);
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

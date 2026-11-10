import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { PagedResult } from '../../programs/program.model';
import { Report, ReportStatus, reportReasons } from '../discussion.model';
import { DiscussionsService } from '../discussions.service';

/** Admins review reported posts: dismiss the report, or hide the post (closing every open report on it). */
@Component({
  selector: 'app-reports',
  imports: [RouterLink, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold">Reported posts</h1>
        <select class="input w-40" [value]="status()" (change)="status.set($any($event.target).value)">
          <option value="Open">Open</option>
          <option value="Actioned">Actioned</option>
          <option value="Dismissed">Dismissed</option>
        </select>
      </div>

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
                  <a [routerLink]="['/programs', report.programId, 'discussion', report.threadId]" class="text-sm text-indigo-600 hover:underline">
                    {{ report.threadTitle }}
                  </a>
                </div>
                @if (report.note) {
                  <p class="mt-2 text-sm text-slate-700">“{{ report.note }}”</p>
                }
                <blockquote class="mt-3 whitespace-pre-line rounded border-l-4 border-slate-300 bg-slate-50 p-3 text-sm text-slate-800">
                  <span class="mb-1 block text-xs text-slate-500">{{ report.postAuthorName }} wrote@if (report.postIsHidden) { · now hidden}:</span>
                  {{ report.postBody || '(deleted)' }}
                </blockquote>

                @if (report.status === 'Open') {
                  <div class="mt-4 flex gap-2">
                    <button class="btn-danger" [disabled]="busy()" (click)="action(report)">Hide post</button>
                    <button class="btn-secondary" [disabled]="busy()" (click)="dismiss(report)">Dismiss</button>
                  </div>
                }
              </li>
            } @empty {
              <li class="text-slate-500">No {{ status().toLowerCase() }} reports.</li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class ReportsComponent {
  private readonly discussions = inject(DiscussionsService);

  protected readonly status = signal<ReportStatus>('Open');
  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly reports = toSignal(
    toObservable(computed(() => ({ status: this.status(), refresh: this.refresh() }))).pipe(
      switchMap(({ status }) => this.discussions.reports(status).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<Report>> },
  );

  protected reasonLabel(report: Report): string {
    return reportReasons.find((r) => r.value === report.reason)?.label ?? report.reason;
  }

  protected action(report: Report): Promise<void> {
    return this.run(this.discussions.actionReport(report.id), 'Post hidden and its open reports closed.');
  }

  protected dismiss(report: Report): Promise<void> {
    return this.run(this.discussions.dismissReport(report.id), 'Report dismissed.');
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

import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, firstValueFrom, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { PagedResult } from '../../programs/program.model';
import { ApplicationStatus, TrainerApplication } from '../trainer.model';
import { TrainersService } from '../trainers.service';

/** Admins approve or reject trainer applications. */
@Component({
  selector: 'app-trainer-applications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold">Trainer applications</h1>
        <select class="input w-40" [value]="status()" (change)="status.set($any($event.target).value)">
          <option value="Pending">Pending</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
        </select>
      </div>

      @if (notice(); as message) {
        <p class="mt-4 text-sm text-emerald-700">{{ message }}</p>
      }
      @if (error(); as message) {
        <p class="mt-4 text-sm text-rose-600">{{ message }}</p>
      }

      @switch (applications().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load applications.</p>
        }
      }

      @if (applications(); as state) {
        @if (state.status === 'ok') {
          <ul class="mt-6 space-y-4">
            @for (application of state.value.data; track application.id) {
              <li class="card">
                <div class="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <h2 class="font-semibold">{{ application.displayName }}</h2>
                    <p class="text-xs text-slate-500">Submitted {{ application.submittedAt.slice(0, 10) }} · user {{ application.userId }}</p>
                  </div>
                  <span class="rounded bg-slate-100 px-2 py-0.5 text-xs">{{ application.status }}</span>
                </div>
                @if (application.bio) {
                  <p class="mt-3 whitespace-pre-line text-sm text-slate-700">{{ application.bio }}</p>
                }
                @if (application.specialties.length) {
                  <p class="mt-2 text-sm"><span class="text-slate-500">Specialties:</span> {{ application.specialties.join(', ') }}</p>
                }
                @for (link of application.links; track link) {
                  <a [href]="link" target="_blank" rel="noopener noreferrer" class="mt-1 block text-sm text-indigo-600 underline">{{ link }}</a>
                }
                @if (application.rejectionReason) {
                  <p class="mt-2 text-sm text-rose-700">Rejected: {{ application.rejectionReason }}</p>
                }

                @if (application.status === 'Pending') {
                  <div class="mt-4 flex flex-wrap items-center gap-2">
                    <button class="btn-primary" [disabled]="busy()" (click)="approve(application)">Approve</button>
                    <input #reason class="input max-w-xs" placeholder="Reason for rejecting" />
                    <button class="btn-danger" [disabled]="busy()" (click)="reject(application, reason.value)">Reject</button>
                  </div>
                }
              </li>
            } @empty {
              <li class="text-slate-500">No {{ status().toLowerCase() }} applications.</li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class TrainerApplicationsComponent {
  private readonly trainers = inject(TrainersService);

  protected readonly status = signal<ApplicationStatus>('Pending');
  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly applications = toSignal(
    toObservable(computed(() => ({ status: this.status(), refresh: this.refresh() }))).pipe(
      switchMap(({ status }) => this.trainers.applications(status).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<TrainerApplication>> },
  );

  protected approve(application: TrainerApplication): Promise<void> {
    return this.run(
      this.trainers.approve(application.id),
      `Approved ${application.displayName}. They get the trainer role within a few seconds and see it after signing in again.`,
    );
  }

  protected reject(application: TrainerApplication, reason: string): Promise<void> {
    return this.run(this.trainers.reject(application.id, reason.trim()), `Rejected ${application.displayName}.`);
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

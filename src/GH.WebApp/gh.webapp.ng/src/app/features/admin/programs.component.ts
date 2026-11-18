import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, firstValueFrom, map, merge, switchMap } from 'rxjs';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { formatRating } from '../reviews/reviews.model';
import { PagedResult, ProgramStatus } from '../programs/program.model';
import { AdminNavComponent } from './admin-nav.component';
import { AdminProgram, adminPageSize, formatAdminPrice } from './admin.model';
import { AdminService } from './admin.service';

/** Admin: every program in any status; published ones can be archived. */
@Component({
  selector: 'app-admin-programs',
  imports: [DatePipe, RouterLink, AdminNavComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Programs</h1>
      <app-admin-nav class="mt-4 block" />

      <div class="mt-6 flex flex-wrap gap-3">
        <input
          class="input max-w-md"
          type="search"
          placeholder="Search by title or trainer"
          aria-label="Search programs"
          [value]="search()"
          (input)="setSearch($any($event.target).value)"
        />
        <select class="input w-44" aria-label="Status" (change)="setStatus($any($event.target).value)">
          <option value="">All statuses</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </div>
      <p class="mt-2 text-sm text-slate-500">Archiving takes a program out of the catalog and stops renewals. Buyers keep access. It can't be undone.</p>

      @if (error(); as message) {
        <p class="mt-3 text-sm text-rose-600">{{ message }}</p>
      }

      @switch (page().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load programs.</p>
        }
      }

      @if (result(); as r) {
        @if (r.data.length === 0) {
          <p class="mt-6 text-slate-500">No programs match.</p>
        } @else {
          <div class="mt-4 overflow-x-auto">
            <table class="w-full text-left text-sm">
              <thead class="border-b border-slate-200 text-slate-500">
                <tr>
                  <th class="py-2 pr-4 font-medium">Program</th>
                  <th class="py-2 pr-4 font-medium">Trainer</th>
                  <th class="py-2 pr-4 font-medium">Status</th>
                  <th class="py-2 pr-4 font-medium">Price</th>
                  <th class="py-2 pr-4 font-medium">Rating</th>
                  <th class="py-2 font-medium"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (program of r.data; track program.id) {
                  <tr class="border-b border-slate-100 align-top">
                    <td class="py-3 pr-4">
                      @if (program.status === 'Published') {
                        <a [routerLink]="['/programs', program.id]" class="font-medium hover:text-indigo-700">{{ program.title }}</a>
                      } @else {
                        <span class="font-medium">{{ program.title }}</span>
                      }
                      <span class="block text-xs text-slate-500">Created {{ program.createdAt | date: 'mediumDate' }}</span>
                    </td>
                    <td class="py-3 pr-4">
                      <a [routerLink]="['/trainers', program.trainerId]" class="hover:text-indigo-700">{{ program.trainerName ?? 'Unknown trainer' }}</a>
                    </td>
                    <td class="py-3 pr-4">
                      {{ program.status }}
                      @if (program.publishedAt) {
                        <span class="block text-xs text-slate-500">Published {{ program.publishedAt | date: 'mediumDate' }}</span>
                      }
                    </td>
                    <td class="py-3 pr-4">{{ formatAdminPrice(program) }}</td>
                    <td class="py-3 pr-4">{{ formatRating(program.rating) }}</td>
                    <td class="py-3 text-right">
                      @if (program.status !== 'Archived') {
                        <button class="btn-secondary" [disabled]="busy()" (click)="archive(program)">Archive</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="mt-4 flex items-center gap-3 text-sm">
            <button class="btn-secondary" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Previous</button>
            <span class="text-slate-500">Page {{ pageIndex() + 1 }} of {{ pageCount() }}</span>
            <button class="btn-secondary" [disabled]="pageIndex() + 1 >= pageCount()" (click)="pageIndex.set(pageIndex() + 1)">Next</button>
          </div>
        }
      }
    </section>
  `,
})
export class ProgramsComponent {
  private readonly admin = inject(AdminService);

  protected readonly formatAdminPrice = formatAdminPrice;
  protected readonly formatRating = formatRating;
  protected readonly statuses: ProgramStatus[] = ['Published', 'Draft', 'Archived'];

  protected readonly search = signal('');
  protected readonly status = signal<ProgramStatus | null>(null);
  protected readonly pageIndex = signal(0);
  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly page = toSignal(
    merge(
      toObservable(this.search).pipe(debounceTime(300), distinctUntilChanged(), map(() => 0)),
      toObservable(computed(() => ({ status: this.status(), pageIndex: this.pageIndex(), refresh: this.refresh() }))).pipe(map(() => 1)),
    ).pipe(switchMap(() => this.admin.programs(this.search(), this.status(), this.pageIndex()).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<PagedResult<AdminProgram>> },
  );

  protected readonly result = computed(() => {
    const state = this.page();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil((this.result()?.count ?? 0) / adminPageSize)));

  protected setSearch(value: string): void {
    this.search.set(value);
    this.pageIndex.set(0);
  }

  protected setStatus(value: string): void {
    this.status.set((value || null) as ProgramStatus | null);
    this.pageIndex.set(0);
  }

  protected async archive(program: AdminProgram): Promise<void> {
    if (!confirm(`Archive "${program.title}"? It leaves the catalog and renewals stop. Buyers keep access. This can't be undone.`)) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.admin.archiveProgram(program.id));
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

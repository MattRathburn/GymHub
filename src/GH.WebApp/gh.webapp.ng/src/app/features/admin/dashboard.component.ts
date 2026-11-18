import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { AdminNavComponent } from './admin-nav.component';
import { AdminMetric, formatMetric, groupMetrics } from './admin.model';
import { AdminService } from './admin.service';

/** Admin home: a few numbers from every module, each linking to the page that shows the detail. */
@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, AdminNavComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Admin</h1>
      <app-admin-nav class="mt-4 block" />

      @switch (state().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load the dashboard.</p>
        }
      }

      @for (group of groups(); track group.section) {
        <h2 class="mt-8 text-sm font-semibold uppercase tracking-wide text-slate-500">{{ group.section }}</h2>
        <div class="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          @for (metric of group.metrics; track $index) {
            @if (metric.link) {
              <a [routerLink]="linkPath(metric.link)" [queryParams]="linkQuery(metric.link)" class="card block hover:border-indigo-300">
                <span class="block text-2xl font-semibold">{{ formatMetric(metric) }}</span>
                <span class="mt-1 block text-sm text-slate-600">{{ metric.label }}</span>
              </a>
            } @else {
              <div class="card">
                <span class="block text-2xl font-semibold">{{ formatMetric(metric) }}</span>
                <span class="mt-1 block text-sm text-slate-600">{{ metric.label }}</span>
              </div>
            }
          }
        </div>
      }
    </section>
  `,
})
export class DashboardComponent {
  private readonly admin = inject(AdminService);

  protected readonly formatMetric = formatMetric;

  protected readonly state = toSignal(this.admin.metrics().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<AdminMetric[]>,
  });

  protected readonly groups = computed(() => {
    const state = this.state();
    return state.status === 'ok' ? groupMetrics(state.value) : [];
  });

  protected linkPath(link: string): string {
    return link.split('?')[0];
  }

  protected linkQuery(link: string): Record<string, string> | null {
    const query = link.split('?')[1];
    return query ? Object.fromEntries(new URLSearchParams(query)) : null;
  }
}

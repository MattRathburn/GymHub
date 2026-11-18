import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Tabs across the admin pages. */
@Component({
  selector: 'app-admin-nav',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="flex flex-wrap gap-4 border-b border-slate-200 text-sm" aria-label="Admin">
      @for (link of links; track link.path) {
        <a
          [routerLink]="link.path"
          routerLinkActive="border-indigo-600 text-indigo-700"
          [routerLinkActiveOptions]="{ exact: true }"
          class="-mb-px border-b-2 border-transparent pb-2 text-slate-600 hover:text-slate-900"
          >{{ link.label }}</a
        >
      }
    </nav>
  `,
})
export class AdminNavComponent {
  protected readonly links = [
    { path: '/admin', label: 'Dashboard' },
    { path: '/admin/users', label: 'Users' },
    { path: '/admin/programs', label: 'Programs' },
    { path: '/admin/trainer-applications', label: 'Trainer applications' },
    { path: '/admin/reports', label: 'Reports' },
    { path: '/admin/purchases', label: 'Purchases' },
  ];
}

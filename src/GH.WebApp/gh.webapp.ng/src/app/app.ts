import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IsActiveMatchOptions, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { DeletionBannerComponent } from './features/account/deletion-banner.component';
import { NotificationBellComponent } from './features/notifications/notification-bell.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NotificationBellComponent, DeletionBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly year = new Date().getFullYear();

  /** Active on exactly this page, whatever its filters, sort or page in the query string. */
  protected readonly samePage: IsActiveMatchOptions = { paths: 'exact', queryParams: 'ignored', matrixParams: 'ignored', fragment: 'ignored' };
}

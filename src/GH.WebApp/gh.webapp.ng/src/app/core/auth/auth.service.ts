import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Observable, catchError, of, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UserProfile } from './user-profile.model';

const anonymous: UserProfile = { isAuthenticated: false, name: '', subject: '', roles: [] };

/** The signed-in user as the Gateway (BFF) sees it. Login and logout are full-page redirects through the Gateway. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly user$: Observable<UserProfile> = this.http
    .get<UserProfile>(`${environment.bffBaseUrl}/api/user`)
    .pipe(
      catchError(() => of(anonymous)),
      shareReplay(1),
    );

  /** Null until the Gateway has answered. */
  readonly user = toSignal(this.user$, { initialValue: null });
  readonly isAuthenticated = computed(() => this.user()?.isAuthenticated ?? false);
  readonly isTrainer = computed(() => this.hasRole('trainer') || this.hasRole('admin'));
  readonly isAdmin = computed(() => this.hasRole('admin'));

  whenLoaded(): Observable<UserProfile> {
    return this.user$;
  }

  login(returnUrl = globalThis.location.pathname + globalThis.location.search): void {
    globalThis.location.href = `${environment.bffBaseUrl}/login?redirectUrl=${encodeURIComponent(returnUrl)}`;
  }

  logout(): void {
    globalThis.location.href = `${environment.bffBaseUrl}/logout?redirectUrl=${encodeURIComponent('/')}`;
  }

  private hasRole(role: string): boolean {
    return this.user()?.roles.includes(role) ?? false;
  }
}

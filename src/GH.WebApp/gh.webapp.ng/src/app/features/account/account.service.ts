import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AccountStatus } from './account.model';

/**
 * The member's account. Keeps the last status in a signal so the site-wide deletion banner and the account page
 * agree after either one changes it.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/account`;

  /** A plain link: the browser downloads the file with the session cookie. */
  readonly exportUrl = `${this.baseUrl}/export`;

  /** Null until loaded (or when loading failed). */
  readonly status = signal<AccountStatus | null>(null);

  refresh(): Observable<AccountStatus | null> {
    return this.http.get<AccountStatus>(`${this.baseUrl}/`).pipe(
      tap((status) => this.status.set(status)),
      catchError(() => of(null)),
    );
  }

  scheduleDeletion(confirmation: string): Observable<{ scheduledFor: string }> {
    return this.http.post<{ scheduledFor: string }>(`${this.baseUrl}/deletion`, { confirmation });
  }

  cancelDeletion(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/deletion`);
  }
}

import { Injectable, Signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { defer, catchError, Observable, of, shareReplay } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';

const ANONYMOUS: Session = null;
const CACHE_SIZE = 1;

@Injectable({
  providedIn: 'root'
})
export class AuthenticationService {
  private readonly http = inject(HttpClient);
  private session$: Observable<Session> | null = null

  public session: Signal<Session> = toSignal(
    defer(() => this.getSession()), // Defer the getSession call
    { initialValue: ANONYMOUS }
  );

  public isAuthenticated = computed(() => this.session() !== null);
  public isAnonymous = computed(() => this.session() === null);
  public username = computed(() => {
    const session = this.session();
    return session ? session.find(c => c.type === 'name')?.value || null : null;
  });
  public logoutUrl = computed(() => {
    const session = this.session();
    return session ? session.find(c => c.type === 'bff:logout_url')?.value || null : null;
  });


  public getSession(ignoreCache: boolean = false): Observable<Session> {
    if (!this.session$ || ignoreCache) {
      this.session$ = this.http.get<Session>('bff/user').pipe(
        catchError(err => of(ANONYMOUS)),
        shareReplay(CACHE_SIZE)
      );
    }
    return this.session$;
  }
}

export interface Claim {
  type: string;
  value: string;
}
export type Session = Claim[] | null;
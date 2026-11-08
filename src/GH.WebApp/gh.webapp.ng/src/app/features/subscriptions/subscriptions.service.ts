import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MyEnrollment } from './subscription.model';

@Injectable({ providedIn: 'root' })
export class SubscriptionsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/subscriptions`;

  mine(): Observable<MyEnrollment[]> {
    return this.http.get<MyEnrollment[]>(`${this.baseUrl}/mine`);
  }

  /** Free programs only; paid ones answer 402 until checkout exists. */
  enroll(programId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/programs/${programId}`, null);
  }

  leave(programId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/programs/${programId}`);
  }
}

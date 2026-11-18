import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult, ProgramStatus } from '../programs/program.model';
import { AdminMetric, AdminProgram, AdminUser, adminPageSize } from './admin.model';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly accountUrl = `${environment.apiBaseUrl}/account/admin`;
  private readonly programsUrl = `${environment.apiBaseUrl}/programs`;
  private readonly trainersUrl = `${environment.apiBaseUrl}/trainers`;

  metrics(): Observable<AdminMetric[]> {
    return this.http.get<AdminMetric[]>(`${this.accountUrl}/metrics`);
  }

  users(search: string, pageIndex: number): Observable<PagedResult<AdminUser>> {
    let params = new HttpParams().set('pageIndex', pageIndex).set('pageSize', adminPageSize);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<PagedResult<AdminUser>>(`${this.accountUrl}/users`, { params });
  }

  suspend(userId: string, reason: string | null): Observable<void> {
    return this.http.post<void>(`${this.accountUrl}/users/${encodeURIComponent(userId)}/suspension`, { reason });
  }

  reactivate(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.accountUrl}/users/${encodeURIComponent(userId)}/suspension`);
  }

  revokeTrainer(userId: string): Observable<void> {
    return this.http.post<void>(`${this.trainersUrl}/${encodeURIComponent(userId)}/revoke`, null);
  }

  programs(search: string, status: ProgramStatus | null, pageIndex: number): Observable<PagedResult<AdminProgram>> {
    let params = new HttpParams().set('pageIndex', pageIndex).set('pageSize', adminPageSize);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<PagedResult<AdminProgram>>(`${this.programsUrl}/admin`, { params });
  }

  archiveProgram(programId: string): Observable<void> {
    return this.http.post<void>(`${this.programsUrl}/${programId}/archive`, null);
  }
}

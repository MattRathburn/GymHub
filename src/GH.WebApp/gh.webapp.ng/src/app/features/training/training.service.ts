import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult } from '../programs/program.model';
import { Adherence, ExerciseProgress, ExerciseSummary, SavedSession, Session, SessionRequest, SessionSummary } from './training.model';

@Injectable({ providedIn: 'root' })
export class TrainingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/training`;

  sessions(options: { programId?: string | null; pageIndex?: number; pageSize?: number } = {}): Observable<PagedResult<SessionSummary>> {
    let params = new HttpParams();

    if (options.programId) {
      params = params.set('programId', options.programId);
    }
    if (options.pageIndex) {
      params = params.set('pageIndex', options.pageIndex);
    }
    if (options.pageSize) {
      params = params.set('pageSize', options.pageSize);
    }

    return this.http.get<PagedResult<SessionSummary>>(`${this.baseUrl}/sessions`, { params });
  }

  session(id: string): Observable<Session> {
    return this.http.get<Session>(`${this.baseUrl}/sessions/${id}`);
  }

  log(request: SessionRequest): Observable<SavedSession> {
    return this.http.post<SavedSession>(`${this.baseUrl}/sessions`, request);
  }

  update(id: string, request: SessionRequest): Observable<SavedSession> {
    return this.http.put<SavedSession>(`${this.baseUrl}/sessions/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sessions/${id}`);
  }

  exercises(): Observable<ExerciseSummary[]> {
    return this.http.get<ExerciseSummary[]>(`${this.baseUrl}/exercises`);
  }

  progress(key: string): Observable<ExerciseProgress> {
    return this.http.get<ExerciseProgress>(`${this.baseUrl}/exercises/${encodeURIComponent(key)}/progress`);
  }

  adherence(programId: string): Observable<Adherence> {
    return this.http.get<Adherence>(`${this.baseUrl}/programs/${programId}/adherence`);
  }
}

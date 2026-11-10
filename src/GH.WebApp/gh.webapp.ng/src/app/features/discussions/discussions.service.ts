import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult } from '../programs/program.model';
import {
  Board,
  ModerateThreadRequest,
  Report,
  ReportReason,
  ReportStatus,
  StartThreadRequest,
  StartedThread,
  Thread,
  ThreadKind,
} from './discussion.model';

export interface BoardQuery {
  kind?: ThreadKind;
  q?: string;
  pageIndex?: number;
}

@Injectable({ providedIn: 'root' })
export class DiscussionsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/discussions`;

  board(programId: string, query: BoardQuery = {}): Observable<Board> {
    let params = new HttpParams().set('pageSize', 20).set('pageIndex', query.pageIndex ?? 0);
    if (query.kind) {
      params = params.set('kind', query.kind);
    }
    if (query.q?.trim()) {
      params = params.set('q', query.q.trim());
    }
    return this.http.get<Board>(`${this.baseUrl}/programs/${programId}/threads`, { params });
  }

  startThread(programId: string, request: StartThreadRequest): Observable<StartedThread> {
    return this.http.post<StartedThread>(`${this.baseUrl}/programs/${programId}/threads`, request);
  }

  thread(threadId: string): Observable<Thread> {
    return this.http.get<Thread>(`${this.baseUrl}/threads/${threadId}`);
  }

  reply(threadId: string, parentPostId: string, body: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/threads/${threadId}/posts`, { parentPostId, body });
  }

  editPost(postId: string, body: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/posts/${postId}`, { body });
  }

  deletePost(postId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/posts/${postId}`);
  }

  moderateThread(threadId: string, request: ModerateThreadRequest): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/threads/${threadId}/moderation`, request);
  }

  setPostHidden(postId: string, hidden: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/posts/${postId}/hidden`, { hidden });
  }

  /** Null clears the answer. */
  markAnswer(threadId: string, postId: string | null): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/threads/${threadId}/answer`, { postId });
  }

  report(postId: string, reason: ReportReason, note: string | null): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/posts/${postId}/reports`, { reason, note });
  }

  // Admin

  reports(status: ReportStatus = 'Open'): Observable<PagedResult<Report>> {
    const params = new HttpParams().set('status', status).set('pageSize', 50);
    return this.http.get<PagedResult<Report>>(`${this.baseUrl}/reports`, { params });
  }

  dismissReport(reportId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reports/${reportId}/dismiss`, null);
  }

  actionReport(reportId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reports/${reportId}/action`, null);
  }
}

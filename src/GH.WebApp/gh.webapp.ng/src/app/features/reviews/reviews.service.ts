import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ReportReason, ReportStatus } from '../discussions/discussion.model';
import { PagedResult } from '../programs/program.model';
import { MyReview, ProgramReviews, Review, ReviewReport, WriteReviewRequest } from './reviews.model';

@Injectable({ providedIn: 'root' })
export class ReviewsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/reviews`;

  forProgram(programId: string, pageIndex = 0): Observable<ProgramReviews> {
    const params = new HttpParams().set('pageSize', 10).set('pageIndex', pageIndex);
    return this.http.get<ProgramReviews>(`${this.baseUrl}/programs/${programId}`, { params });
  }

  mine(programId: string): Observable<MyReview> {
    return this.http.get<MyReview>(`${this.baseUrl}/programs/${programId}/mine`);
  }

  write(programId: string, request: WriteReviewRequest): Observable<Review> {
    return this.http.put<Review>(`${this.baseUrl}/programs/${programId}/mine`, request);
  }

  remove(programId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/programs/${programId}/mine`);
  }

  reply(reviewId: string, body: string): Observable<Review> {
    return this.http.put<Review>(`${this.baseUrl}/${reviewId}/reply`, { body });
  }

  removeReply(reviewId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${reviewId}/reply`);
  }

  report(reviewId: string, reason: ReportReason, note: string | null): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${reviewId}/reports`, { reason, note });
  }

  reports(status: ReportStatus): Observable<PagedResult<ReviewReport>> {
    const params = new HttpParams().set('status', status).set('pageSize', 50);
    return this.http.get<PagedResult<ReviewReport>>(`${this.baseUrl}/reports/`, { params });
  }

  dismissReport(reportId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reports/${reportId}/dismiss`, null);
  }

  actionReport(reportId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reports/${reportId}/action`, null);
  }

  /** Admins: show a hidden review again. */
  showReview(reviewId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reports/reviews/${reviewId}/show`, null);
  }
}

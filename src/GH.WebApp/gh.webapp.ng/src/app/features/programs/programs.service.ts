import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BrowseQuery,
  MediaTarget,
  MyProgramRow,
  PagedResult,
  PresignedUpload,
  ProgramCard,
  ProgramContent,
  ProgramDetailsRequest,
  ProgramOverview,
  ProgramPrice,
  WorkoutRequest,
} from './program.model';

@Injectable({ providedIn: 'root' })
export class ProgramsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/programs`;

  browse(query: BrowseQuery): Observable<PagedResult<ProgramCard>> {
    let params = new HttpParams()
      .set('pageIndex', query.pageIndex ?? 0)
      .set('pageSize', query.pageSize ?? 24);

    if (query.search) {
      params = params.set('search', query.search);
    }
    if (query.free !== null && query.free !== undefined) {
      params = params.set('free', query.free);
    }
    if (query.kind) {
      params = params.set('kind', query.kind);
    }

    return this.http.get<PagedResult<ProgramCard>>(`${this.baseUrl}/`, { params });
  }

  overview(id: string): Observable<ProgramOverview> {
    return this.http.get<ProgramOverview>(`${this.baseUrl}/${id}`);
  }

  /** Released content; 401 when signed out, 403 when not subscribed. */
  content(id: string): Observable<ProgramContent> {
    return this.http.get<ProgramContent>(`${this.baseUrl}/${id}/content`);
  }

  // Trainer studio

  mine(): Observable<MyProgramRow[]> {
    return this.http.get<MyProgramRow[]>(`${this.baseUrl}/mine`);
  }

  forEditing(id: string): Observable<ProgramContent> {
    return this.http.get<ProgramContent>(`${this.baseUrl}/${id}/edit`);
  }

  create(request: ProgramDetailsRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.baseUrl}/`, request);
  }

  updateDetails(id: string, request: ProgramDetailsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, request);
  }

  setPricing(id: string, price: ProgramPrice): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/pricing`, price);
  }

  addWeek(id: string, title: string, releasesOn: string): Observable<{ weekId: string; number: number }> {
    return this.http.post<{ weekId: string; number: number }>(`${this.baseUrl}/${id}/weeks`, { title, releasesOn });
  }

  updateWeek(id: string, weekId: string, title: string, releasesOn: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/weeks/${weekId}`, { title, releasesOn });
  }

  removeWeek(id: string, weekId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/weeks/${weekId}`);
  }

  upsertDay(id: string, weekId: string, dayNumber: number, day: { title: string | null; isRestDay: boolean; notes: string | null }): Observable<{ dayId: string }> {
    return this.http.put<{ dayId: string }>(`${this.baseUrl}/${id}/weeks/${weekId}/days/${dayNumber}`, day);
  }

  saveWorkout(id: string, weekId: string, dayNumber: number, workoutId: string | null, workout: WorkoutRequest): Observable<unknown> {
    const url = `${this.baseUrl}/${id}/weeks/${weekId}/days/${dayNumber}/workouts`;
    return workoutId ? this.http.put(`${url}/${workoutId}`, workout) : this.http.post(url, workout);
  }

  removeWorkout(id: string, weekId: string, dayNumber: number, workoutId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/weeks/${weekId}/days/${dayNumber}/workouts/${workoutId}`);
  }

  requestUpload(id: string, file: File): Observable<PresignedUpload> {
    return this.http.post<PresignedUpload>(`${this.baseUrl}/${id}/media/uploads`, { fileName: file.name, contentType: file.type });
  }

  /** PUTs the file straight to object storage with the presigned URL. */
  upload(upload: PresignedUpload, file: File): Observable<unknown> {
    return this.http.put(upload.uploadUrl, file, { headers: { 'Content-Type': file.type } });
  }

  attachMedia(id: string, target: MediaTarget, targetId: string | null, objectKey: string, contentType: string, caption: string | null): Observable<{ mediaId: string }> {
    return this.http.post<{ mediaId: string }>(`${this.baseUrl}/${id}/media`, { target, targetId, objectKey, contentType, caption });
  }

  publish(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/publish`, null);
  }

  archive(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/archive`, null);
  }
}

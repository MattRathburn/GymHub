import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult, PresignedUpload } from '../programs/program.model';
import {
  ApplicationStatus,
  FollowedTrainer,
  MemberProfile,
  MyTrainerProfile,
  TrainerApplication,
  TrainerDetailsRequest,
  TrainerProfile,
} from './trainer.model';

@Injectable({ providedIn: 'root' })
export class TrainersService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/trainers`;

  // Applying

  apply(request: TrainerDetailsRequest): Observable<TrainerApplication> {
    return this.http.post<TrainerApplication>(`${this.baseUrl}/applications`, request);
  }

  /** The user's latest application; 404 when they never applied. */
  myApplication(): Observable<TrainerApplication> {
    return this.http.get<TrainerApplication>(`${this.baseUrl}/applications/mine`);
  }

  // Admin review

  applications(status: ApplicationStatus = 'Pending'): Observable<PagedResult<TrainerApplication>> {
    const params = new HttpParams().set('status', status).set('pageSize', 50);
    return this.http.get<PagedResult<TrainerApplication>>(`${this.baseUrl}/applications`, { params });
  }

  approve(applicationId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/applications/${applicationId}/approve`, null);
  }

  reject(applicationId: string, reason: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/applications/${applicationId}/reject`, { reason });
  }

  // Profiles and following

  profile(trainerId: string): Observable<TrainerProfile> {
    return this.http.get<TrainerProfile>(`${this.baseUrl}/${encodeURIComponent(trainerId)}`);
  }

  follow(trainerId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${encodeURIComponent(trainerId)}/follow`, null);
  }

  unfollow(trainerId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(trainerId)}/follow`);
  }

  following(): Observable<FollowedTrainer[]> {
    return this.http.get<FollowedTrainer[]>(`${this.baseUrl}/following`);
  }

  // Trainer studio

  /** 404 until the trainer saves a profile. */
  myProfile(): Observable<MyTrainerProfile> {
    return this.http.get<MyTrainerProfile>(`${this.baseUrl}/me/profile`);
  }

  saveMyProfile(request: TrainerDetailsRequest): Observable<MyTrainerProfile> {
    return this.http.put<MyTrainerProfile>(`${this.baseUrl}/me/profile`, request);
  }

  requestAvatarUpload(file: File): Observable<PresignedUpload> {
    return this.http.post<PresignedUpload>(`${this.baseUrl}/me/avatar/uploads`, { fileName: file.name, contentType: file.type });
  }

  setAvatar(objectKey: string): Observable<MyTrainerProfile> {
    return this.http.put<MyTrainerProfile>(`${this.baseUrl}/me/avatar`, { objectKey });
  }

  // Member profile (every user)

  /** 404 until the user saves a display name. */
  myMemberProfile(): Observable<MemberProfile> {
    return this.http.get<MemberProfile>(`${this.baseUrl}/me/member-profile`);
  }

  saveMyMemberProfile(displayName: string): Observable<MemberProfile> {
    return this.http.put<MemberProfile>(`${this.baseUrl}/me/member-profile`, { displayName });
  }

  requestMemberAvatarUpload(file: File): Observable<PresignedUpload> {
    return this.http.post<PresignedUpload>(`${this.baseUrl}/me/member-profile/avatar/uploads`, { fileName: file.name, contentType: file.type });
  }

  setMemberAvatar(objectKey: string): Observable<MemberProfile> {
    return this.http.put<MemberProfile>(`${this.baseUrl}/me/member-profile/avatar`, { objectKey });
  }
}

import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { ProgramsService } from '../programs/programs.service';
import { MemberProfile } from './trainer.model';
import { TrainersService } from './trainers.service';

/** Any user sets the name and avatar shown next to their discussion posts. */
@Component({
  selector: 'app-member-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Your profile</h1>
      <p class="mt-1 text-sm text-slate-600">This is how you appear on program discussion boards.</p>
      @if (auth.isTrainer()) {
        <p class="mt-2 text-sm text-slate-500">As a trainer, your trainer profile name is shown instead.</p>
      }

      @if (!loaded()) {
        <p class="mt-6 text-slate-500">Loading…</p>
      } @else {
        <div class="card mt-6 space-y-4">
          <div class="flex items-center gap-4">
            @if (profile()?.avatarUrl; as url) {
              <img [src]="url" alt="" class="h-16 w-16 rounded-full object-cover" />
            } @else {
              <div class="flex h-16 w-16 items-center justify-center rounded-full bg-slate-200 text-xl text-slate-500">
                {{ (profile()?.displayName ?? auth.user()?.name ?? '?').charAt(0).toUpperCase() }}
              </div>
            }
            @if (profile()) {
              <label class="btn-secondary cursor-pointer">
                Upload avatar
                <input type="file" accept="image/*" class="hidden" [disabled]="busy()" (change)="uploadAvatar($event)" />
              </label>
            }
          </div>

          <div>
            <label class="label" for="displayName">Display name</label>
            <input #name id="displayName" class="input" maxlength="40" [value]="profile()?.displayName ?? auth.user()?.name ?? ''" />
          </div>
          <button class="btn-primary" [disabled]="busy()" (click)="save(name.value)">Save</button>
        </div>
      }

      @if (notice(); as message) {
        <p class="mt-4 text-sm text-emerald-700">{{ message }}</p>
      }
      @if (error(); as message) {
        <p class="mt-4 text-sm text-rose-600">{{ message }}</p>
      }
    </section>
  `,
})
export class MemberProfileComponent {
  private readonly trainers = inject(TrainersService);
  private readonly programsService = inject(ProgramsService);
  protected readonly auth = inject(AuthService);

  /** undefined while loading, null when the user has no profile yet. */
  private readonly state = signal<MemberProfile | null | undefined>(undefined);
  protected readonly loaded = computed(() => this.state() !== undefined);
  protected readonly profile = computed(() => this.state() ?? null);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.trainers.myMemberProfile().subscribe({
      next: (profile) => this.state.set(profile),
      error: (error: unknown) => {
        this.state.set(null);
        if (!(error instanceof HttpErrorResponse && error.status === 404)) {
          this.error.set(problemMessage(error));
        }
      },
    });
  }

  protected async save(displayName: string): Promise<void> {
    await this.run(async () => {
      this.state.set(await firstValueFrom(this.trainers.saveMyMemberProfile(displayName.trim())));
      this.notice.set('Profile saved.');
    });
  }

  protected async uploadAvatar(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) {
      return;
    }

    await this.run(async () => {
      const upload = await firstValueFrom(this.trainers.requestMemberAvatarUpload(file));
      await firstValueFrom(this.programsService.upload(upload, file));
      this.state.set(await firstValueFrom(this.trainers.setMemberAvatar(upload.objectKey)));
      this.notice.set('Avatar updated.');
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

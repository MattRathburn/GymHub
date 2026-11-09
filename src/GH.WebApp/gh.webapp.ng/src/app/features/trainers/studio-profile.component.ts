import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { ProgramsService } from '../programs/programs.service';
import { MyTrainerProfile, TrainerDetailsRequest } from './trainer.model';
import { TrainerDetailsFormComponent } from './trainer-details-form.component';
import { TrainersService } from './trainers.service';

/** Trainer studio: edit the public profile and avatar. */
@Component({
  selector: 'app-studio-profile',
  imports: [RouterLink, TrainerDetailsFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-2xl px-4 py-8">
      <div class="flex items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold">Your trainer profile</h1>
        @if (profile(); as p) {
          <a [routerLink]="['/trainers', p.userId]" class="text-sm text-indigo-600 underline">View public profile</a>
        }
      </div>

      @if (loaded()) {
        @if (profile(); as p) {
          <div class="card mt-6 flex items-center gap-4">
            @if (p.avatarUrl) {
              <img [src]="p.avatarUrl" alt="" class="h-20 w-20 rounded-full object-cover" />
            } @else {
              <div class="flex h-20 w-20 items-center justify-center rounded-full bg-indigo-100 text-2xl font-semibold text-indigo-700">
                {{ p.displayName.charAt(0) }}
              </div>
            }
            <label class="btn-secondary cursor-pointer">
              Upload avatar
              <input type="file" accept="image/*" class="hidden" [disabled]="busy()" (change)="uploadAvatar($event)" />
            </label>
          </div>
        } @else {
          <p class="mt-2 text-slate-600">You don't have a public profile yet. Save one so members can find and follow you.</p>
        }

        <div class="mt-6">
          <app-trainer-details-form [initial]="profile()" submitLabel="Save profile" [busy]="busy()" (save)="save($event)" />
        </div>
      } @else {
        <p class="mt-6 text-slate-500">Loading…</p>
      }

      @if (notice(); as message) {
        <p class="mt-2 text-sm text-emerald-700">{{ message }}</p>
      }
      @if (error(); as message) {
        <p class="mt-2 text-sm text-rose-600">{{ message }}</p>
      }
    </section>
  `,
})
export class StudioProfileComponent {
  private readonly trainers = inject(TrainersService);
  private readonly programsService = inject(ProgramsService);

  /** undefined while loading, null when the trainer has no profile yet. */
  private readonly state = signal<MyTrainerProfile | null | undefined>(undefined);
  protected readonly loaded = computed(() => this.state() !== undefined);
  protected readonly profile = computed(() => this.state() ?? null);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.trainers.myProfile().subscribe({
      next: (profile) => this.state.set(profile),
      error: (error: unknown) => {
        this.state.set(null);
        if (!(error instanceof HttpErrorResponse && error.status === 404)) {
          this.error.set(problemMessage(error));
        }
      },
    });
  }

  protected async save(request: TrainerDetailsRequest): Promise<void> {
    await this.run(async () => {
      this.state.set(await firstValueFrom(this.trainers.saveMyProfile(request)));
      this.notice.set('Profile saved.');
    });
  }

  protected async uploadAvatar(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) {
      return;
    }

    await this.run(async () => {
      const upload = await firstValueFrom(this.trainers.requestAvatarUpload(file));
      await firstValueFrom(this.programsService.upload(upload, file));
      this.state.set(await firstValueFrom(this.trainers.setAvatar(upload.objectKey)));
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

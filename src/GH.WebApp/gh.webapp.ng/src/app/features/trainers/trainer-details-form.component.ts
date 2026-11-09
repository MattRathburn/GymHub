import { ChangeDetectionStrategy, Component, effect, inject, input, output } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TrainerDetailsRequest, splitList } from './trainer.model';

/** Display name, bio, specialties and links: used by the apply form and the studio profile editor. */
@Component({
  selector: 'app-trainer-details-form',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="card grid gap-4" [formGroup]="form" (ngSubmit)="submit()">
      <div>
        <label class="label" for="displayName">Display name</label>
        <input id="displayName" class="input" formControlName="displayName" maxlength="80" />
      </div>
      <div>
        <label class="label" for="bio">About you</label>
        <textarea id="bio" class="input min-h-32" formControlName="bio" maxlength="2000"
          placeholder="Your coaching background, certifications and approach"></textarea>
      </div>
      <div>
        <label class="label" for="specialties">Specialties</label>
        <input id="specialties" class="input" formControlName="specialties" placeholder="Powerlifting, Mobility, Hypertrophy" />
        <p class="mt-1 text-xs text-slate-500">Separate with commas. Up to 10.</p>
      </div>
      <div>
        <label class="label" for="links">Links</label>
        <textarea id="links" class="input" formControlName="links" placeholder="https://instagram.com/you"></textarea>
        <p class="mt-1 text-xs text-slate-500">One full URL per line. Up to 5.</p>
      </div>
      <div>
        <button class="btn-primary" type="submit" [disabled]="form.invalid || busy()">{{ submitLabel() }}</button>
      </div>
    </form>
  `,
})
export class TrainerDetailsFormComponent {
  readonly initial = input<Partial<TrainerDetailsRequest> | null>(null);
  readonly submitLabel = input('Save');
  readonly busy = input(false);
  readonly save = output<TrainerDetailsRequest>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(80)]],
    bio: ['', Validators.maxLength(2000)],
    specialties: [''],
    links: [''],
  });

  constructor() {
    effect(() => {
      const initial = this.initial();
      if (initial) {
        this.form.setValue({
          displayName: initial.displayName ?? '',
          bio: initial.bio ?? '',
          specialties: (initial.specialties ?? []).join(', '),
          links: (initial.links ?? []).join('\n'),
        });
      }
    });
  }

  protected submit(): void {
    const v = this.form.getRawValue();
    this.save.emit({
      displayName: v.displayName.trim(),
      bio: v.bio.trim() || null,
      specialties: splitList(v.specialties),
      links: splitList(v.links),
    });
  }
}

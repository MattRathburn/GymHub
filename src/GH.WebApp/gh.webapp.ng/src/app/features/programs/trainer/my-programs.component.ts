import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LoadState, toLoadState } from '../../../core/http/load-state';
import { problemMessage } from '../../../core/http/problem';
import { MyProgramRow, ProgramKind, formatPrice } from '../program.model';
import { ProgramsService } from '../programs.service';

@Component({
  selector: 'app-my-programs',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <h1 class="text-2xl font-semibold">Trainer studio</h1>

      <form class="card mt-6 grid gap-3 sm:grid-cols-4" [formGroup]="form" (ngSubmit)="create()">
        <div class="sm:col-span-2">
          <label class="label" for="title">New program</label>
          <input id="title" class="input" formControlName="title" placeholder="e.g. 12-week strength base" />
        </div>
        <div>
          <label class="label" for="kind">Kind</label>
          <select id="kind" class="input" formControlName="kind">
            <option value="Ongoing">Ongoing</option>
            <option value="Finite">Fixed dates</option>
          </select>
        </div>
        @if (form.controls.kind.value === 'Finite') {
          <div class="grid grid-cols-2 gap-2 sm:col-span-4">
            <div>
              <label class="label" for="startsOn">Starts on</label>
              <input id="startsOn" type="date" class="input" formControlName="startsOn" />
            </div>
            <div>
              <label class="label" for="endsOn">Ends on</label>
              <input id="endsOn" type="date" class="input" formControlName="endsOn" />
            </div>
          </div>
        }
        <div class="flex items-end">
          <button class="btn-primary w-full" type="submit" [disabled]="form.invalid || busy()">Create</button>
        </div>
        @if (error(); as message) {
          <p class="text-sm text-rose-600 sm:col-span-4">{{ message }}</p>
        }
      </form>

      @switch (programs().status) {
        @case ('loading') {
          <p class="mt-6 text-slate-500">Loading…</p>
        }
        @case ('error') {
          <p class="mt-6 text-rose-600">Couldn't load your programs.</p>
        }
      }

      @if (programs(); as state) {
        @if (state.status === 'ok') {
          <table class="mt-6 w-full overflow-hidden rounded-lg border border-slate-200 bg-white text-sm">
            <thead class="bg-slate-100 text-left text-slate-600">
              <tr>
                <th class="px-3 py-2">Program</th>
                <th class="px-3 py-2">Status</th>
                <th class="px-3 py-2">Weeks</th>
                <th class="px-3 py-2">Price</th>
              </tr>
            </thead>
            <tbody>
              @for (program of state.value; track program.id) {
                <tr class="border-t border-slate-200">
                  <td class="px-3 py-2">
                    <a [routerLink]="['/studio/programs', program.id]" class="font-medium text-indigo-700 hover:underline">{{ program.title }}</a>
                  </td>
                  <td class="px-3 py-2">{{ program.status }}</td>
                  <td class="px-3 py-2">{{ program.weekCount }}</td>
                  <td class="px-3 py-2">{{ formatPrice(program.price) }}</td>
                </tr>
              } @empty {
                <tr><td class="px-3 py-4 text-slate-500" colspan="4">No programs yet.</td></tr>
              }
            </tbody>
          </table>
        }
      }
    </section>
  `,
})
export class MyProgramsComponent {
  private readonly programsService = inject(ProgramsService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formatPrice = formatPrice;

  protected readonly form = this.fb.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    kind: ['Ongoing' as ProgramKind],
    startsOn: [''],
    endsOn: [''],
  });

  protected readonly programs = toSignal(this.programsService.mine().pipe(toLoadState()), {
    initialValue: { status: 'loading' } as LoadState<MyProgramRow[]>,
  });

  protected async create(): Promise<void> {
    const value = this.form.getRawValue();
    const finite = value.kind === 'Finite';

    this.busy.set(true);
    this.error.set(null);
    try {
      const created = await firstValueFrom(
        this.programsService.create({
          title: value.title,
          summary: null,
          description: null,
          kind: value.kind,
          startsOn: finite ? value.startsOn || null : null,
          endsOn: finite ? value.endsOn || null : null,
        }),
      );
      await this.router.navigate(['/studio/programs', created.id]);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { SubscriptionsService } from '../subscriptions/subscriptions.service';
import { ProgramContent, ProgramOverview, formatPrice } from './program.model';
import { ProgramsService } from './programs.service';
import { WorkoutViewComponent } from './workout-view.component';

@Component({
  selector: 'app-program-detail',
  imports: [RouterLink, WorkoutViewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './program-detail.component.html',
})
export class ProgramDetailComponent {
  private readonly programsService = inject(ProgramsService);
  private readonly subscriptions = inject(SubscriptionsService);
  protected readonly auth = inject(AuthService);

  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly formatPrice = formatPrice;

  protected readonly overview = toSignal(
    toObservable(this.id).pipe(switchMap((id) => this.programsService.overview(id).pipe(toLoadState()))),
    { initialValue: { status: 'loading' } as LoadState<ProgramOverview> },
  );

  /** 401 = signed out, 403 = not subscribed. */
  protected readonly content = toSignal(
    toObservable(computed(() => ({ id: this.id(), refresh: this.refresh() }))).pipe(
      switchMap(({ id }) => this.programsService.content(id).pipe(toLoadState())),
    ),
    { initialValue: { status: 'loading' } as LoadState<ProgramContent> },
  );

  protected readonly program = computed(() => {
    const state = this.overview();
    return state.status === 'ok' ? state.value : null;
  });

  protected readonly releasedWeeks = computed(() => {
    const state = this.content();
    return state.status === 'ok' ? state.value.weeks : [];
  });

  protected readonly hasAccess = computed(() => this.content().status === 'ok');
  protected readonly needsSignIn = computed(() => this.contentErrorCode() === 401);
  protected readonly canSubscribe = computed(() => this.contentErrorCode() === 403);
  protected readonly upcomingWeeks = computed(() => this.program()?.weeks.filter((w) => !w.isReleased) ?? []);
  protected readonly isOwner = computed(() => {
    const state = this.content();
    return state.status === 'ok' && state.value.trainerId === this.auth.user()?.subject;
  });

  protected async subscribe(): Promise<void> {
    await this.run(() => this.subscriptions.enroll(this.id()));
  }

  protected async leave(): Promise<void> {
    await this.run(() => this.subscriptions.leave(this.id()));
  }

  private contentErrorCode(): number | null {
    const state = this.content();
    return state.status === 'error' ? state.code : null;
  }

  private async run(action: () => ReturnType<SubscriptionsService['enroll']>): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(action());
      this.refresh.update((n) => n + 1);
    } catch (error) {
      this.actionError.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

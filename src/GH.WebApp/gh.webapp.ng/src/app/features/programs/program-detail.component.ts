import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, catchError, firstValueFrom, of, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { LoadState, toLoadState } from '../../core/http/load-state';
import { problemMessage } from '../../core/http/problem';
import { Purchase, Redirect, givesAccess, purchaseStatusLine } from '../billing/billing.model';
import { BillingService, goToStripe } from '../billing/billing.service';
import { SubscriptionsService } from '../subscriptions/subscriptions.service';
import { ProgramContent, ProgramOverview, formatPrice, formatTrial } from './program.model';
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
  private readonly billing = inject(BillingService);
  protected readonly auth = inject(AuthService);

  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  /** Query parameter Stripe Checkout returns with: "success" or "cancelled". */
  readonly checkout = input<string>();

  /** True while waiting for the payment webhook after returning from Checkout. */
  protected readonly confirmingPayment = signal(false);
  protected readonly paymentSlow = signal(false);

  private readonly refresh = signal(0);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly formatPrice = formatPrice;
  protected readonly formatTrial = formatTrial;
  protected readonly purchaseStatusLine = purchaseStatusLine;

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

  /** The member's purchase of this program, if one gives access (so they manage it in billing, not with Leave). */
  private readonly purchases = toSignal(
    toObservable(computed(() => ({ signedIn: this.auth.isAuthenticated(), refresh: this.refresh() }))).pipe(
      switchMap(({ signedIn }) =>
        signedIn ? this.billing.mine().pipe(catchError(() => of([] as Purchase[]))) : of([] as Purchase[]),
      ),
    ),
    { initialValue: [] as Purchase[] },
  );

  protected readonly paidAccess = computed(
    () => this.purchases().find((p) => p.programId === this.id() && givesAccess(p)) ?? null,
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

  constructor() {
    const destroyRef = inject(DestroyRef);
    let timer: ReturnType<typeof setTimeout> | undefined;
    destroyRef.onDestroy(() => clearTimeout(timer));

    // Back from Checkout: access arrives with Stripe's webhook, usually within seconds, so check a few times.
    effect(() => {
      if (this.checkout() !== 'success') {
        return;
      }

      untracked(() => {
        let attempts = 0;
        this.confirmingPayment.set(true);

        const poll = () => {
          if (this.hasAccess()) {
            this.confirmingPayment.set(false);
            return;
          }

          if (++attempts > 10) {
            this.confirmingPayment.set(false);
            this.paymentSlow.set(true);
            return;
          }

          this.refresh.update((n) => n + 1);
          timer = setTimeout(poll, 2000);
        };

        timer = setTimeout(poll, 1000);
      });
    });
  }

  protected buy(): Promise<void> {
    return this.redirect(this.billing.checkout(this.id()));
  }

  protected manageBilling(): Promise<void> {
    return this.redirect(this.billing.portal());
  }

  protected async subscribe(): Promise<void> {
    await this.run(() => this.subscriptions.enroll(this.id()));
  }

  protected async leave(): Promise<void> {
    await this.run(() => this.subscriptions.leave(this.id()));
  }

  private async redirect(request: Observable<Redirect>): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      goToStripe((await firstValueFrom(request)).url);
    } catch (error) {
      this.actionError.set(problemMessage(error));
      this.busy.set(false);
    }
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

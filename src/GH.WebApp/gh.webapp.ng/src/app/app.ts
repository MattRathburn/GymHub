import { Component, signal, WritableSignal, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';

interface ClaimDto {
  type: string;
  value: string;
}

interface UserProfile {
  isAuthenticated: boolean;
  name: string;
  subject: string;
  roles: string[];
  claims: ClaimDto[];
}

interface PlanComponent {
  id: number;
  name: string;
  description?: string;
}

interface PlanCreator {
  id: number;
  name?: string;
}

interface GHPlan {
  id: number;
  name: string;
  description?: string;
  creator?: PlanCreator;
  components?: PlanComponent[];
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);

  isAuthenticated: WritableSignal<boolean> = signal(false);
  userProfile: WritableSignal<UserProfile | null> = signal(null);
  popularPlans: WritableSignal<GHPlan[]> = signal([]);
  subscribedPlans: WritableSignal<GHPlan[]> = signal([]);
  busy: WritableSignal<boolean> = signal(false);
  error: WritableSignal<string | null> = signal(null);

  ngOnInit(): void {
    void this.initAsync();
  }

  private async initAsync() {
    this.busy.set(true);
    try {
      await this.loadUserProfile();
      await this.loadPopularPlans();
      if (this.isAuthenticated()) {
        await this.loadSubscribedPlans();
      }
    } catch (e) {
      console.error(e);
      this.error.set((e as Error)?.message ?? 'Unknown error');
    } finally {
      this.busy.set(false);
    }
  }

  private getGatewayUrl(path: string): string {
    return `${location.protocol}//${location.host}${path}`;
  }

  async loadUserProfile() {
    try {
      const data = await this.http.get<UserProfile>(this.getGatewayUrl('/api/user'), { withCredentials: true }).toPromise();
      this.userProfile.set(data ?? null);
      this.isAuthenticated.set(data?.isAuthenticated ?? false);
    } catch (err) {
      console.warn('Unable to fetch user profile, not logged in.', err);
      this.userProfile.set(null);
      this.isAuthenticated.set(false);
    }
  }

  async loadPopularPlans() {
    const plans = await this.http
      .get<GHPlan[]>(this.getGatewayUrl('/planapi/api/plan/popular?count=8'), { withCredentials: true })
      .toPromise();
    this.popularPlans.set(plans ?? []);
  }

  async loadSubscribedPlans() {
    if (!this.isAuthenticated()) {
      this.subscribedPlans.set([]);
      return;
    }

    const plans = await this.http
      .get<GHPlan[]>(this.getGatewayUrl('/planapi/api/plan/subscribed'), { withCredentials: true })
      .toPromise();

    this.subscribedPlans.set(plans ?? []);
  }

  async subscribe(planId: number) {
    try {
      await this.http
        .post(this.getGatewayUrl(`/planapi/api/plan/${planId}/subscribe`), null, { withCredentials: true })
        .toPromise();
      await this.loadSubscribedPlans();
    } catch (err) {
      this.error.set('Subscription failed.');
      console.error(err);
    }
  }

  async unsubscribe(planId: number) {
    try {
      await this.http
        .delete(this.getGatewayUrl(`/planapi/api/plan/${planId}/unsubscribe`), { withCredentials: true })
        .toPromise();
      await this.loadSubscribedPlans();
    } catch (err) {
      this.error.set('Unsubscribe failed.');
      console.error(err);
    }
  }

  login() {
    const redirect = encodeURIComponent(location.pathname + location.search);
    location.href = this.getGatewayUrl(`/login?redirectUrl=${redirect}`);
  }

  logout() {
    const redirect = encodeURIComponent(location.pathname + location.search);
    location.href = this.getGatewayUrl(`/logout?redirectUrl=${redirect}`);
  }

  isSubscribed(planId: number): boolean {
    return this.subscribedPlans().some((plan) => plan.id === planId);
  }
}

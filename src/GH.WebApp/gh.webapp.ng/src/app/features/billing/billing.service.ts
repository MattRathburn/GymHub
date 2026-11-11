import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult } from '../programs/program.model';
import { PayoutStatus, Purchase, PurchaseFilter, Redirect } from './billing.model';

@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/billing`;

  payoutStatus(): Observable<PayoutStatus> {
    return this.http.get<PayoutStatus>(`${this.baseUrl}/payouts`);
  }

  /** A Stripe-hosted onboarding page for the trainer's Express account. */
  startOnboarding(): Observable<Redirect> {
    return this.http.post<Redirect>(`${this.baseUrl}/payouts/onboarding`, null);
  }

  payoutDashboard(): Observable<Redirect> {
    return this.http.post<Redirect>(`${this.baseUrl}/payouts/dashboard`, null);
  }

  /** A Stripe Checkout page for a paid program. Access arrives by webhook after payment. */
  checkout(programId: string): Observable<Redirect> {
    return this.http.post<Redirect>(`${this.baseUrl}/programs/${programId}/checkout`, null);
  }

  /** Stripe's Customer Portal: cancel subscriptions, update the card, see invoices. */
  portal(): Observable<Redirect> {
    return this.http.post<Redirect>(`${this.baseUrl}/portal`, null);
  }

  mine(): Observable<Purchase[]> {
    return this.http.get<Purchase[]>(`${this.baseUrl}/me/purchases`);
  }

  sales(filter: PurchaseFilter = {}): Observable<PagedResult<Purchase>> {
    return this.http.get<PagedResult<Purchase>>(`${this.baseUrl}/sales`, { params: toParams(filter) });
  }

  allPurchases(filter: PurchaseFilter = {}): Observable<PagedResult<Purchase>> {
    return this.http.get<PagedResult<Purchase>>(`${this.baseUrl}/purchases`, { params: toParams(filter) });
  }

  refund(purchaseId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/purchases/${purchaseId}/refund`, null);
  }
}

function toParams(filter: PurchaseFilter): HttpParams {
  let params = new HttpParams();

  for (const [key, value] of Object.entries(filter)) {
    if (value !== null && value !== undefined && value !== '') {
      params = params.set(key, String(value));
    }
  }

  return params;
}

/** Sends the browser to a Stripe-hosted page. */
export function goToStripe(url: string): void {
  window.location.assign(url);
}

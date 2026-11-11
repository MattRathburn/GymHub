import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Purchase, nextPriceLine, purchaseStatusLine } from './billing.model';
import { BillingService } from './billing.service';

describe('BillingService', () => {
  let service: BillingService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(BillingService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('starts checkout for a program', () => {
    service.checkout('p1').subscribe();

    expect(backend.expectOne('/gh-api/api/v1/billing/programs/p1/checkout').request.method).toBe('POST');
  });

  it('filters purchases, omitting empty filters', () => {
    service.allPurchases({ status: 'Refunded', programId: null, pageIndex: 2 }).subscribe();

    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/billing/purchases').request;
    expect(request.params.get('status')).toBe('Refunded');
    expect(request.params.get('pageIndex')).toBe('2');
    expect(request.params.has('programId')).toBe(false);
  });

  it('refunds a purchase', () => {
    service.refund('pu1').subscribe();

    expect(backend.expectOne('/gh-api/api/v1/billing/purchases/pu1/refund').request.method).toBe('POST');
  });
});

describe('purchase lines', () => {
  const base: Purchase = {
    id: 'pu1',
    programId: 'p1',
    programTitle: 'Squat Lab',
    userId: 'u1',
    buyerName: null,
    kind: 'Subscription',
    status: 'Active',
    amountMinor: 1999,
    currency: 'USD',
    interval: 'Monthly',
    feeMinor: 200,
    createdAt: '2026-11-01T00:00:00Z',
    lastPaymentAt: '2026-11-01T00:00:00Z',
    currentPeriodEnd: '2026-12-01T00:00:00Z',
    cancelAtPeriodEnd: false,
    trialEndsAt: null,
    nextAmountMinor: null,
    nextInterval: null,
    canRefund: false,
  };

  it('says when a subscription renews or ends', () => {
    expect(purchaseStatusLine(base)).toContain('Renews');
    expect(purchaseStatusLine({ ...base, cancelAtPeriodEnd: true })).toContain('Ends');
    expect(purchaseStatusLine({ ...base, kind: 'OneTime' })).toBe('Purchased');
  });

  it('shows an upcoming price only while the subscription renews', () => {
    expect(nextPriceLine(base)).toBeNull();
    expect(nextPriceLine({ ...base, nextAmountMinor: 2499, nextInterval: 'Monthly' })).toContain('/ month');
    expect(nextPriceLine({ ...base, nextAmountMinor: 2499, cancelAtPeriodEnd: true })).toBeNull();
  });
});

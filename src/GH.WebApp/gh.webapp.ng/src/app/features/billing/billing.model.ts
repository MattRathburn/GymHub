/** Mirrors GymHub.Modules.Billing DTOs. Enums are sent as strings. */
import { BillingInterval } from '../programs/program.model';

export type PayoutSetup = 'NotStarted' | 'InProgress' | 'Ready';
export type PurchaseKind = 'OneTime' | 'Subscription';
export type PurchaseStatus = 'Pending' | 'Trialing' | 'Active' | 'PastDue' | 'Ended' | 'Refunded' | 'Disputed';

export interface PayoutStatus {
  setup: PayoutSetup;
  chargesEnabled: boolean;
  payoutsEnabled: boolean;
  detailsSubmitted: boolean;
  platformFeePercent: number;
}

export interface Redirect {
  url: string;
}

export interface Purchase {
  id: string;
  programId: string;
  programTitle: string;
  userId: string;
  buyerName: string | null;
  kind: PurchaseKind;
  status: PurchaseStatus;
  amountMinor: number;
  currency: string;
  interval: BillingInterval | null;
  feeMinor: number;
  createdAt: string;
  lastPaymentAt: string | null;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  trialEndsAt: string | null;
  nextAmountMinor: number | null;
  nextInterval: BillingInterval | null;
  canRefund: boolean;
}

export interface PurchaseFilter {
  status?: PurchaseStatus | null;
  programId?: string | null;
  pageIndex?: number;
  pageSize?: number;
}

export function givesAccess(purchase: Purchase): boolean {
  return purchase.status === 'Trialing' || purchase.status === 'Active' || purchase.status === 'PastDue';
}

export function formatMoney(amountMinor: number, currency: string, interval: BillingInterval | null = null): string {
  const amount = new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amountMinor / 100);
  return interval ? `${amount} / ${interval === 'Yearly' ? 'year' : 'month'}` : amount;
}

function day(iso: string): string {
  return new Date(iso).toLocaleDateString();
}

/** One line on where a member's purchase stands, e.g. "Free trial until 1/2/2027". */
export function purchaseStatusLine(purchase: Purchase): string {
  switch (purchase.status) {
    case 'Trialing':
      return purchase.trialEndsAt ? `Free trial until ${day(purchase.trialEndsAt)}` : 'Free trial';
    case 'PastDue':
      return 'Payment failed: update your card in Manage billing';
    case 'Active':
      if (purchase.kind === 'OneTime') {
        return 'Purchased';
      }
      if (purchase.currentPeriodEnd) {
        return purchase.cancelAtPeriodEnd ? `Ends ${day(purchase.currentPeriodEnd)}` : `Renews ${day(purchase.currentPeriodEnd)}`;
      }
      return 'Subscribed';
    case 'Ended':
      return 'Ended';
    case 'Refunded':
      return 'Refunded';
    case 'Disputed':
      return 'Payment disputed';
    default:
      return 'Checkout not finished';
  }
}

/** The upcoming price change of a subscription, if any. */
export function nextPriceLine(purchase: Purchase): string | null {
  if (purchase.nextAmountMinor === null || purchase.cancelAtPeriodEnd || !givesAccess(purchase)) {
    return null;
  }

  const price = formatMoney(purchase.nextAmountMinor, purchase.currency, purchase.nextInterval);
  return purchase.currentPeriodEnd ? `Renews at ${price} from ${day(purchase.currentPeriodEnd)}` : `Renews at ${price}`;
}

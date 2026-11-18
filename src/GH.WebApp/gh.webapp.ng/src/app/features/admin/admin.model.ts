/** Mirrors the admin DTOs of the Accounts and Programs modules. */

import { BillingInterval, PricingModel, ProgramKind, ProgramRating, ProgramStatus, formatPrice } from '../programs/program.model';

export interface AdminMetric {
  section: string;
  label: string;
  value: number;
  /** Set for money, in minor units. */
  currency: string | null;
  link: string | null;
}

export const metricSections = ['Users', 'Programs', 'Money', 'Moderation'] as const;

export interface AdminUser {
  id: string;
  username: string;
  email: string | null;
  displayName: string | null;
  emailVerified: boolean;
  enabled: boolean;
  createdAt: string | null;
  isTrainer: boolean;
  isAdmin: boolean;
  suspension: { suspendedAt: string; reason: string | null } | null;
  deletionScheduledFor: string | null;
}

export interface AdminProgram {
  id: string;
  title: string;
  trainerId: string;
  trainerName: string | null;
  status: ProgramStatus;
  kind: ProgramKind;
  createdAt: string;
  publishedAt: string | null;
  priceModel: PricingModel;
  priceAmountMinor: number | null;
  priceCurrency: string | null;
  priceInterval: BillingInterval | null;
  rating: ProgramRating;
}

export const adminPageSize = 25;

/** The dashboard's metrics grouped by section, in a fixed section order (unknown sections last). */
export function groupMetrics(metrics: AdminMetric[]): { section: string; metrics: AdminMetric[] }[] {
  const order = (section: string) => {
    const index = (metricSections as readonly string[]).indexOf(section);
    return index === -1 ? metricSections.length : index;
  };

  const sections = [...new Set(metrics.map((m) => m.section))].sort((a, b) => order(a) - order(b));
  return sections.map((section) => ({ section, metrics: metrics.filter((m) => m.section === section) }));
}

export function formatMetric(metric: AdminMetric): string {
  return metric.currency
    ? new Intl.NumberFormat(undefined, { style: 'currency', currency: metric.currency }).format(metric.value / 100)
    : new Intl.NumberFormat().format(metric.value);
}

export function formatAdminPrice(program: AdminProgram): string {
  return formatPrice({
    model: program.priceModel,
    amountMinor: program.priceAmountMinor,
    currency: program.priceCurrency,
    interval: program.priceInterval,
  });
}

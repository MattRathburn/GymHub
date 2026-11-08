import { ProgramStatus } from '../programs/program.model';

export interface MyEnrollment {
  programId: string;
  programTitle: string | null;
  programStatus: ProgramStatus | null;
  source: 'Free' | 'Stripe';
  enrolledAt: string;
}

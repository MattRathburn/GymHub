import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** A readable message from an API error (ProblemDetails or ValidationProblemDetails). */
export function problemMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong.';
  }

  const problem = error.error as ProblemDetails | null;

  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(' ');
  }

  return problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}

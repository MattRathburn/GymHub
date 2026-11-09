import { HttpErrorResponse } from '@angular/common/http';
import { Observable, OperatorFunction, catchError, map, of, startWith } from 'rxjs';
import { problemMessage } from './problem';

/** What a component shows for one request: loading, the value, or the error. */
export type LoadState<T> =
  | { status: 'loading' }
  | { status: 'ok'; value: T }
  | { status: 'error'; code: number; message: string };

/** Wraps an HTTP stream so templates can switch on its state and errors never escape the stream. */
export function toLoadState<T>(): OperatorFunction<T, LoadState<T>> {
  return (source: Observable<T>) =>
    source.pipe(
      map((value): LoadState<T> => ({ status: 'ok', value })),
      catchError((error: unknown) =>
        of<LoadState<T>>({
          status: 'error',
          code: error instanceof HttpErrorResponse ? error.status : 0,
          message: problemMessage(error),
        }),
      ),
      startWith<LoadState<T>>({ status: 'loading' }),
    );
}

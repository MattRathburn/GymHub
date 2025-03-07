import { HttpInterceptorFn } from '@angular/common/http';

export const csrfHeaderInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req);
};

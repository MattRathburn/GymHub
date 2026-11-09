import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

/** Sends anonymous users to the Gateway login, then back here. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);

  return auth.whenLoaded().pipe(
    map((user) => {
      if (!user.isAuthenticated) {
        auth.login(state.url);
        return false;
      }
      return true;
    }),
  );
};

/** Trainer studio: trainers and admins only. */
export const trainerGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.whenLoaded().pipe(
    map((user) => {
      if (!user.isAuthenticated) {
        auth.login(state.url);
        return false;
      }
      return user.roles.includes('trainer') || user.roles.includes('admin') ? true : router.parseUrl('/');
    }),
  );
};

/** Admin screens: admins only. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.whenLoaded().pipe(
    map((user) => {
      if (!user.isAuthenticated) {
        auth.login(state.url);
        return false;
      }
      return user.roles.includes('admin') ? true : router.parseUrl('/');
    }),
  );
};

import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, of } from 'rxjs';
import { AuthService } from './auth.service';
import { adminGuard } from './auth.guards';
import { UserProfile } from './user-profile.model';

describe('adminGuard', () => {
  const login = vi.fn();

  function run(user: UserProfile): Promise<boolean | UrlTree> {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { whenLoaded: () => of(user), login } }],
    });

    const result = TestBed.runInInjectionContext(() =>
      adminGuard({} as ActivatedRouteSnapshot, { url: '/admin/trainer-applications' } as RouterStateSnapshot),
    ) as Observable<boolean | UrlTree>;

    return firstValueFrom(result);
  }

  beforeEach(() => login.mockReset());

  it('lets admins in', async () => {
    expect(await run({ isAuthenticated: true, name: 'Alex', subject: 'a', roles: ['admin'] })).toBe(true);
  });

  it('sends other signed-in users home', async () => {
    const result = await run({ isAuthenticated: true, name: 'Morgan', subject: 'm', roles: ['trainer'] });

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('sends anonymous users to sign in and back', async () => {
    expect(await run({ isAuthenticated: false, name: '', subject: '', roles: [] })).toBe(false);
    expect(login).toHaveBeenCalledWith('/admin/trainer-applications');
  });
});

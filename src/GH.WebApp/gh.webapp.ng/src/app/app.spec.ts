import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('offers sign in when the Gateway says the user is anonymous', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/user')
      .flush({ isAuthenticated: false, name: '', subject: '', roles: [] });
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Sign in');
    expect(text).not.toContain('Trainer studio');
  });

  it('shows the trainer studio to trainers', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/user')
      .flush({ isAuthenticated: true, name: 'trainer', subject: 't-1', roles: ['trainer'] });
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Trainer studio');
    expect(text).toContain('Sign out');
  });
});

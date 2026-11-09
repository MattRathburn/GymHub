import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TrainerApplicationsComponent } from './trainer-applications.component';

describe('TrainerApplicationsComponent', () => {
  it('loads pending applications and lists them', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(TrainerApplicationsComponent);
    const backend = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/trainers/applications');
    expect(request.request.params.get('status')).toBe('Pending');

    request.flush({
      data: [
        {
          id: 'a1',
          userId: 'u1',
          displayName: 'Sam',
          bio: null,
          specialties: [],
          links: [],
          status: 'Pending',
          submittedAt: '2026-11-09T10:00:00Z',
          reviewedAt: null,
          rejectionReason: null,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Sam');
    backend.verify();
  });
});

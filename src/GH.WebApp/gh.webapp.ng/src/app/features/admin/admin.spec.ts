import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminMetric, formatMetric, groupMetrics } from './admin.model';
import { AdminService } from './admin.service';

const metric = (section: string, label: string, value: number, currency: string | null = null): AdminMetric => ({
  section,
  label,
  value,
  currency,
  link: null,
});

describe('admin model', () => {
  it('groups metrics in the fixed section order, unknown sections last', () => {
    const groups = groupMetrics([
      metric('Moderation', 'Open post reports', 2),
      metric('Other', 'Something', 1),
      metric('Users', 'Users', 10),
      metric('Moderation', 'Open review reports', 0),
    ]);

    expect(groups.map((g) => g.section)).toEqual(['Users', 'Moderation', 'Other']);
    expect(groups[1].metrics.map((m) => m.label)).toEqual(['Open post reports', 'Open review reports']);
  });

  it('formats money from minor units and plain counts', () => {
    expect(formatMetric(metric('Money', 'Sales', 123456, 'USD'))).toContain('1,234.56');
    expect(formatMetric(metric('Users', 'Users', 1500))).toBe(new Intl.NumberFormat().format(1500));
  });
});

describe('AdminService', () => {
  let service: AdminService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('searches users and pages', () => {
    service.users('  alex ', 2).subscribe();

    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/account/admin/users').request;
    expect([request.params.get('search'), request.params.get('pageIndex')]).toEqual(['alex', '2']);
  });

  it('suspends with POST and reactivates with DELETE on the same URL', () => {
    service.suspend('user 1', 'Spam').subscribe();
    service.reactivate('user 1').subscribe();

    const calls = backend.match('/gh-api/api/v1/account/admin/users/user%201/suspension');
    expect(calls.map((c) => c.request.method)).toEqual(['POST', 'DELETE']);
    expect(calls[0].request.body).toEqual({ reason: 'Spam' });
  });

  it('revokes trainers and filters programs by status', () => {
    service.revokeTrainer('t-1').subscribe();
    service.programs('', 'Draft', 0).subscribe();

    expect(backend.expectOne('/gh-api/api/v1/trainers/t-1/revoke').request.method).toBe('POST');
    const programs = backend.expectOne((r) => r.url === '/gh-api/api/v1/programs/admin').request;
    expect([programs.params.get('status'), programs.params.has('search')]).toEqual(['Draft', false]);
  });
});

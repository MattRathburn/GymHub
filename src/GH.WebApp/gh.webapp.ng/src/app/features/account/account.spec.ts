import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { confirmsDeletion } from './account.model';
import { AccountService } from './account.service';

describe('account deletion', () => {
  it('needs the exact phrase, ignoring case and outer spaces', () => {
    expect(confirmsDeletion('  Delete My Account ')).toBe(true);
    expect(confirmsDeletion('delete account')).toBe(false);
    expect(confirmsDeletion('')).toBe(false);
  });

  it('keeps the last status for the banner and the account page', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(AccountService);
    const http = TestBed.inject(HttpTestingController);

    const loaded = firstValueFrom(service.refresh());
    http.expectOne('/gh-api/api/v1/account/').flush({ deletion: { requestedAt: '2026-11-17T00:00:00Z', scheduledFor: '2026-11-24T00:00:00Z' }, deletionBlockers: [] });
    await loaded;
    expect(service.status()?.deletion?.scheduledFor).toBe('2026-11-24T00:00:00Z');

    service.scheduleDeletion('delete my account').subscribe();
    const schedule = http.expectOne('/gh-api/api/v1/account/deletion');
    expect(schedule.request.method).toBe('POST');
    expect(schedule.request.body).toEqual({ confirmation: 'delete my account' });
    http.verify();
  });
});

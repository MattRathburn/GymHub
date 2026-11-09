import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { splitList } from './trainer.model';
import { TrainersService } from './trainers.service';

describe('TrainersService', () => {
  let service: TrainersService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TrainersService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('posts applications', () => {
    service.apply({ displayName: 'Sam', bio: null, specialties: ['Mobility'], links: [] }).subscribe();

    const request = backend.expectOne('/gh-api/api/v1/trainers/applications').request;
    expect(request.method).toBe('POST');
    expect(request.body.displayName).toBe('Sam');
  });

  it('lists applications by status for admins', () => {
    service.applications('Rejected').subscribe();

    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/trainers/applications').request;
    expect(request.params.get('status')).toBe('Rejected');
  });

  it('follows and unfollows with PUT and DELETE', () => {
    service.follow('abc').subscribe();
    service.unfollow('abc').subscribe();

    const calls = backend.match('/gh-api/api/v1/trainers/abc/follow');
    expect(calls.map((c) => c.request.method)).toEqual(['PUT', 'DELETE']);
  });
});

describe('splitList', () => {
  it('splits on commas and newlines and drops blanks', () => {
    expect(splitList(' Powerlifting, Mobility ,\n\nhttps://x.test ')).toEqual(['Powerlifting', 'Mobility', 'https://x.test']);
  });
});

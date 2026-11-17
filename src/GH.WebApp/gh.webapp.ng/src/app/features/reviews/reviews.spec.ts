import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { formatRating, starBars, starPercent } from './reviews.model';
import { ReviewsService } from './reviews.service';

describe('ReviewsService', () => {
  let service: ReviewsService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ReviewsService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('pages through a program’s reviews', () => {
    service.forProgram('p1', 2).subscribe();

    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/reviews/programs/p1');
    expect(request.request.params.get('pageIndex')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('10');
  });

  it('writes and deletes your own review', () => {
    service.write('p1', { stars: 4, body: null }).subscribe();
    service.remove('p1').subscribe();

    const [write, remove] = backend.match('/gh-api/api/v1/reviews/programs/p1/mine');
    expect(write.request.method).toBe('PUT');
    expect(write.request.body).toEqual({ stars: 4, body: null });
    expect(remove.request.method).toBe('DELETE');
  });

  it('replies, reports and moderates by review and report id', () => {
    service.reply('r1', 'Thanks').subscribe();
    service.report('r1', 'Spam', null).subscribe();
    service.actionReport('x1').subscribe();

    expect(backend.expectOne('/gh-api/api/v1/reviews/r1/reply').request.method).toBe('PUT');
    expect(backend.expectOne('/gh-api/api/v1/reviews/r1/reports').request.body).toEqual({ reason: 'Spam', note: null });
    expect(backend.expectOne('/gh-api/api/v1/reviews/reports/x1/action').request.method).toBe('POST');
  });
});

describe('reviews model', () => {
  it('formats ratings for cards and the program page', () => {
    expect(formatRating({ average: null, count: 0 })).toBe('No reviews yet');
    expect(formatRating({ average: 4, count: 1 })).toBe('4.0 · 1 review');
    expect(formatRating({ average: 4.25, count: 12 })).toBe('4.3 · 12 reviews');
  });

  it('fills stars by share of five', () => {
    expect(starPercent(null)).toBe(0);
    expect(starPercent(2.5)).toBe(50);
    expect(starPercent(7)).toBe(100);
  });

  it('lists bars from 5 stars down with their share', () => {
    const bars = starBars({ average: 4.5, count: 4, starCounts: [0, 0, 0, 2, 2] });

    expect(bars.map((b) => b.stars)).toEqual([5, 4, 3, 2, 1]);
    expect(bars[0]).toEqual({ stars: 5, count: 2, percent: 50 });
    expect(bars[4].percent).toBe(0);
    expect(starBars({ average: null, count: 0, starCounts: [0, 0, 0, 0, 0] })[0].percent).toBe(0);
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DiscussionsService } from './discussions.service';

describe('DiscussionsService', () => {
  let service: DiscussionsService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(DiscussionsService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('filters the board by kind and trimmed search, omitting empty filters', () => {
    service.board('p1', { kind: 'Question', q: '  squat ' }).subscribe();
    service.board('p1', { q: '   ' }).subscribe();

    const [filtered, plain] = backend.match((r) => r.url === '/gh-api/api/v1/discussions/programs/p1/threads');
    expect(filtered.request.params.get('kind')).toBe('Question');
    expect(filtered.request.params.get('q')).toBe('squat');
    expect(plain.request.params.has('kind')).toBe(false);
    expect(plain.request.params.has('q')).toBe(false);
  });

  it('replies to a parent post', () => {
    service.reply('t1', 'post-1', 'Hello').subscribe();

    const request = backend.expectOne('/gh-api/api/v1/discussions/threads/t1/posts').request;
    expect(request.method).toBe('POST');
    expect(request.body).toEqual({ parentPostId: 'post-1', body: 'Hello' });
  });

  it('moderates with a partial PATCH and clears the answer with null', () => {
    service.moderateThread('t1', { locked: true }).subscribe();
    service.markAnswer('t1', null).subscribe();

    const moderation = backend.expectOne('/gh-api/api/v1/discussions/threads/t1/moderation').request;
    expect(moderation.method).toBe('PATCH');
    expect(moderation.body).toEqual({ locked: true });

    expect(backend.expectOne('/gh-api/api/v1/discussions/threads/t1/answer').request.body).toEqual({ postId: null });
  });
});

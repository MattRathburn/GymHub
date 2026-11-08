import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { secureApiInterceptor, xsrfCookieName, xsrfHeaderName } from './secure-api.interceptor';

describe('secureApiInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    document.cookie = `${xsrfCookieName}=token-123; path=/`;

    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([secureApiInterceptor])), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the XSRF header and credentials to relative Gateway API calls', () => {
    http.post('/gh-api/api/v1/programs/', {}).subscribe();

    const request = backend.expectOne('/gh-api/api/v1/programs/').request;
    expect(request.headers.get(xsrfHeaderName)).toBe('token-123');
    expect(request.withCredentials).toBe(true);
  });

  it('adds the header to absolute same-origin URLs too', () => {
    const url = `${globalThis.location.origin}/api/user`;
    http.get(url).subscribe();

    expect(backend.expectOne(url).request.headers.get(xsrfHeaderName)).toBe('token-123');
  });

  it('leaves other origins alone, such as presigned storage uploads', () => {
    const url = 'http://storage.example/gymhub-media/programs/x.jpg';
    http.put(url, new Blob()).subscribe();

    const request = backend.expectOne(url).request;
    expect(request.headers.has(xsrfHeaderName)).toBe(false);
    expect(request.withCredentials).toBe(false);
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TrainersService } from '../trainers/trainers.service';
import { browseQueryFor, catalogPageSize, catalogQueryParams, readCatalogState } from './program.model';
import { ProgramsService } from './programs.service';

describe('catalog state', () => {
  it('reads known values from the query string and ignores the rest', () => {
    expect(readCatalogState({ q: '  squat ', pricing: 'OneTime', kind: 'Finite', sort: 'TopRated', page: '3' })).toEqual({
      q: 'squat',
      pricing: 'OneTime',
      kind: 'Finite',
      sort: 'TopRated',
      page: 2,
    });

    expect(readCatalogState({ pricing: 'Cheap', kind: 'x', sort: 'Random', page: '-4' })).toEqual({
      q: '',
      pricing: null,
      kind: null,
      sort: 'Newest',
      page: 0,
    });
  });

  it('leaves defaults out of the URL', () => {
    expect(catalogQueryParams({ q: '', pricing: null, kind: null, sort: 'Newest', page: 0 })).toEqual({
      q: null,
      pricing: null,
      kind: null,
      sort: null,
      page: null,
    });
    expect(catalogQueryParams({ q: 'x', pricing: 'Free', kind: 'Ongoing', sort: 'PriceLow', page: 1 })).toEqual({
      q: 'x',
      pricing: 'Free',
      kind: 'Ongoing',
      sort: 'PriceLow',
      page: '2',
    });
  });

  it('round-trips through the URL', () => {
    const state = { q: 'bench', pricing: 'Recurring' as const, kind: null, sort: 'PriceHigh' as const, page: 4 };
    const params = catalogQueryParams(state);
    expect(readCatalogState({ q: params['q']!, pricing: params['pricing']!, sort: params['sort']!, page: params['page']! })).toEqual(state);
  });

  it('asks the API for one page of the right size', () => {
    expect(browseQueryFor({ q: '', pricing: null, kind: null, sort: 'TopRated', page: 2 })).toEqual({
      search: undefined,
      pricing: null,
      kind: null,
      sort: 'TopRated',
      pageIndex: 2,
      pageSize: catalogPageSize,
    });
  });
});

describe('discovery requests', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends pricing and sort, but not the default sort', () => {
    const programs = TestBed.inject(ProgramsService);
    programs.browse({ pricing: 'OneTime', sort: 'PriceLow' }).subscribe();
    programs.browse({ sort: 'Newest' }).subscribe();

    const [sorted, newest] = backend.match((r) => r.url === '/gh-api/api/v1/programs/');
    expect(sorted.request.params.get('pricing')).toBe('OneTime');
    expect(sorted.request.params.get('sort')).toBe('PriceLow');
    expect(newest.request.params.has('sort')).toBe(false);
  });

  it('searches the trainer directory', () => {
    TestBed.inject(TrainersService).directory(' yoga ', 1).subscribe();

    const request = backend.expectOne((r) => r.url === '/gh-api/api/v1/trainers/');
    expect(request.request.params.get('search')).toBe('yoga');
    expect(request.request.params.get('pageIndex')).toBe('1');
  });
});

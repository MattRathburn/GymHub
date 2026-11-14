import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Workout } from '../programs/program.model';
import {
  ProgressPoint,
  chartPoints,
  describeSet,
  draftFromWorkout,
  estimatedOneRepMax,
  localToday,
  normalizeExerciseKey,
} from './training.model';
import { TrainingService } from './training.service';

describe('TrainingService', () => {
  let service: TrainingService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TrainingService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('filters sessions by program only when asked', () => {
    service.sessions({ programId: 'p1', pageIndex: 2 }).subscribe();
    service.sessions().subscribe();

    const [filtered, all] = backend.match((r) => r.url === '/gh-api/api/v1/training/sessions');
    expect(filtered.request.params.get('programId')).toBe('p1');
    expect(filtered.request.params.get('pageIndex')).toBe('2');
    expect(all.request.params.keys()).toEqual([]);
  });

  it('logs new sessions and updates existing ones', () => {
    const request = { performedOn: '2026-11-11', title: null, notes: null, durationMin: null, programId: null, programWorkoutId: null, exercises: [] };
    service.log(request).subscribe();
    service.update('s1', request).subscribe();

    expect(backend.expectOne('/gh-api/api/v1/training/sessions').request.method).toBe('POST');
    expect(backend.expectOne('/gh-api/api/v1/training/sessions/s1').request.method).toBe('PUT');
  });

  it('escapes exercise keys in the progress url', () => {
    service.progress('a/b').subscribe();
    backend.expectOne('/gh-api/api/v1/training/exercises/a%2Fb/progress');
  });
});

describe('training model', () => {
  it('normalizes names like the server', () => {
    expect(normalizeExerciseKey('  Back   SQUAT ')).toBe('back-squat');
    expect(normalizeExerciseKey('Bench Press (DB)')).toBe('bench-press-db');
    expect(normalizeExerciseKey('Übungs-Satz')).toBe('übungs-satz');
    expect(normalizeExerciseKey('---')).toBe('');
  });

  it('estimates one-rep maxes with Epley for 1 to 12 reps', () => {
    expect(estimatedOneRepMax(1, 100)).toBe(100);
    expect(estimatedOneRepMax(5, 100)).toBe(116.7);
    expect(estimatedOneRepMax(13, 100)).toBeNull();
    expect(estimatedOneRepMax(5, null)).toBeNull();
  });

  it('starts a log from a program workout with its planned sets', () => {
    const workout: Workout = {
      id: 'w1',
      order: 1,
      title: 'Squat day',
      notes: null,
      media: [],
      exercises: [
        { id: 'e1', order: 1, name: 'Back Squat', notes: 'Brace', media: [], sets: [{ reps: 5, weightKg: 100, durationSec: null, distanceM: null, restSec: 120, rpe: 8 }] },
        { id: 'e2', order: 2, name: 'Plank', notes: null, media: [], sets: [] },
      ],
    };

    expect(draftFromWorkout(workout)).toEqual([
      { name: 'Back Squat', programExerciseId: 'e1', notes: null, sets: [{ reps: 5, weightKg: 100, durationSec: null, distanceM: null, rpe: null }] },
      { name: 'Plank', programExerciseId: 'e2', notes: null, sets: [{ reps: null, weightKg: null, durationSec: null, distanceM: null, rpe: null }] },
    ]);
  });

  it('describes sets compactly', () => {
    expect(describeSet({ reps: 5, weightKg: 100, durationSec: null, distanceM: null, rpe: 8 })).toBe('5 × 100 kg · RPE 8');
    expect(describeSet({ reps: 20, weightKg: null, durationSec: null, distanceM: null, rpe: null })).toBe('20 reps');
    expect(describeSet({ reps: null, weightKg: null, durationSec: 1500, distanceM: 5000, rpe: null })).toBe('1500 s · 5 km');
  });

  it('uses the local date for today', () => {
    expect(localToday(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
  });

  it('lays out the chart by date and skips sessions without the metric', () => {
    const point = (performedOn: string, bestE1RmKg: number | null): ProgressPoint => ({
      sessionId: performedOn,
      performedOn,
      bestE1RmKg,
      topWeightKg: null,
      volumeKg: 0,
      totalReps: 0,
    });

    const chart = chartPoints([point('2026-11-01', 100), point('2026-11-02', null), point('2026-11-11', 120)], 'bestE1RmKg', 100, 50);

    expect(chart.map((p) => p.x)).toEqual([0, 100]);
    expect(chart[1].y).toBe(0);
    expect(chart[0].y).toBeGreaterThan(chart[1].y);
    expect(chartPoints([point('2026-11-01', 100)], 'bestE1RmKg', 100, 50)[0].x).toBe(50);
    expect(chartPoints([point('2026-11-01', null)], 'bestE1RmKg', 100, 50)).toEqual([]);
  });
});

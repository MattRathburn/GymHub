# Plan: Training Log module

## Context

Programs tell members what to do; nothing records what they actually did. The Training Log lets members log workouts, freestyle or following a released program workout, and see their history, per-exercise progress, personal records and how much of a program they've done.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| What can be logged | **Program and freestyle workouts.** Starting from a released program workout pre-fills its exercises and planned sets. One history covers both. |
| Views in v1 | **Workout history, per-exercise progress, personal records, program adherence.** |
| Trainer visibility | **Private for now.** Only the member sees their logs. Trainer insight can come later as an opt-in. |

## Findings that shape the design

- **Program ids are stable on edit, but only through the API.** `Workout.ReplaceExercises` keeps exercise ids that are sent back, but the Angular editor could only add and remove workouts, so fixing a typo changed every id. The editor gets "Edit workout", which uses the existing update endpoint.
- **Logs must survive program changes.** Removing a workout or week hard-deletes it, and numbers and order get renumbered. A session snapshots titles and names and keeps program ids only as references, never as foreign keys.
- **No contract exposed program content to other modules.** Programs.Contracts gets `IProgramWorkouts`.
- **Access** follows program content: the program's trainer, an admin, or a member with `IEntitlements` access. Only released weeks count.

## Design

### Programs.Contracts: `IProgramWorkouts`
- `GetReleasedWorkoutAsync(programId, workoutId)` returns `ReleasedWorkout`: program title, week id and number, day number, workout title, and exercises with their planned sets (`PlannedSet`, which mirrors the internal `ExerciseSet`).
- `GetReleasedScheduleAsync(programId)` returns the released weeks with their workouts, for adherence.
- Both return null for drafts, and the first also for unreleased or missing workouts. Callers check access themselves.

### Module `GymHub.Modules.TrainingLog`
- Route prefix and schema `training`. No `.Contracts` project, since nothing needs it yet.
- **`WorkoutSession`:**
  - owner, date (no later than today + 1 day, to allow for time zones), title (defaults to the program workout's title, then "Workout"), notes, duration
  - a snapshot program link: program id and title, week id and number, workout id
  - 1 to 30 exercises
- **`LoggedExercise`:**
  - name, `ExerciseKey`, optional `ProgramExerciseId`, notes, and 1 to 50 sets stored as JSON (`LoggedSet`: reps, kg, seconds, metres, RPE, with at least one of the first four)
  - summary columns computed on save: top weight, best e1RM (Epley, 1 to 12 reps), volume (reps × kg), total reps, set count
  - owner, date and logged-at are copied onto the row, so progress queries read columns, not JSON
- **`ExerciseKey`:** lower-case letters and digits, with every other run of characters as one hyphen. "Back Squat", "back  squat" and the program's "Back Squat" share one progress line.
- **Personal records are derived, not stored:** a session sets a weight or e1RM record when it beats every earlier session for that key (by date, then by when it was logged). The first session is the baseline. Edits and deletes can't leave a stale record.
- **Program exercises take their names from the program.** When editing, they keep the name they were logged with, and any new exercise is freestyle.

### Endpoints (`/api/v1/training`, signed in, owner-scoped)
| Endpoint | What |
|---|---|
| `POST /sessions` | Log (freestyle, or `programId` + `programWorkoutId`). Returns the id and the records it set. |
| `PUT /sessions/{id}`, `DELETE /sessions/{id}` | Edit (returns records) or delete. |
| `GET /sessions?programId=&pageIndex=&pageSize=`, `GET /sessions/{id}` | History and one session. |
| `GET /exercises` | Every logged exercise, with last date, session count and bests. |
| `GET /exercises/{key}/progress` | One point per session, plus the record history. The key is normalized, so any spelling works. |
| `GET /programs/{programId}/adherence` | Released weeks and workouts, each marked with the latest session. Also lists sessions whose workout was later removed. Needs program access. |

### Angular (`features/training/`)
- **History** (`/me/training`, optionally `?programId=`), **session detail** with edit and delete, and the **log form**. The log form handles new, from-program (`/me/training/log?programId=&workoutId=`) and edit, offers name suggestions, adds new sets as copies of the last, and shows the records a save set.
- **Exercises list**, and **exercise progress** with an inline SVG line chart (e1RM, top weight or volume) and the record list.
- **Program page:** "Log workout" per released workout, a "✓ Done" mark, and a progress card linking to that program's sessions.
- **Program editor:** "Edit workout". An exercise row that wasn't changed keeps its exact sets.

## As built

- Built as planned on branch `training-log`, with these differences:
  - **Slices are grouped by resource** (`LogSession`, `EditSession`, `BrowseSessions`, `Exercises`, `ProgramAdherence`) rather than one file per endpoint, as Notifications' `Inbox` does.
  - **`ReleasedWorkout` also carries the program title**, so a session can snapshot it without a second call.
- **Tests:**
  - unit: key normalization, Epley bounds, session summaries and invariants, update keeping the program link, record derivation
  - 5 functional tests:
    - freestyle round trip and owner-only access
    - validation
    - program access, release, snapshotting and foreign exercise ids
    - records across log, edit and delete
    - adherence with a removed workout
  - Angular specs: the service, the model helpers, the draft from a workout and the chart layout

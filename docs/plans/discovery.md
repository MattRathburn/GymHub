# Plan: Discovery

## Context

Milestone 2b of the MVP roadmap (`docs/plans/mvp-roadmap.md`). Before this change:
- The catalog searched titles only.
- It could filter Free or Paid, always sorted newest first, and the page only ever loaded the first 24 programs.
- There was no way to browse trainers.
- `/` was the bare catalog.

### Decisions (from Q&A)

| Topic | Decision |
|---|---|
| Price | Filter by **pricing type** (Any, Free, One-time, Subscription) and **sort by price** low→high or high→low. Amounts are compared as they are, ignoring currency, which is fine while nearly everything is USD. |
| Top rated | A **weighted (Bayesian) average**: each program's rating is pulled toward the site-wide average until it has enough reviews. Unrated programs come last. |
| Trainer directory | **Only trainers with a published program**, showing their program count and their programs' combined rating. |

## Design

### Programs
- **Price copy:** `TrainingProgram` keeps a copy of its current price model and amount (`PriceModel`, `PriceAmountMinor`). It is set wherever `IsFree` is set, and a migration backfills it from `price_versions`.
- **New query options** on `GET /api/v1/programs`:
  - `pricing=Free|OneTime|Recurring`
  - `sort=Newest|TopRated|PriceLow|PriceHigh`
  - `kind` (already supported)
  - The `free` filter stays for existing callers.
- **Search** matches the title or summary, or the trainer's display name. Matching trainer ids come from a new `ITrainerDirectory.FindTrainerIdsAsync` in `Trainers.Contracts`.
- **Top rated** score: `(C·m + sum) / (C + count)`, with prior weight `C = 5` and prior `m = 3` stars (see As built for why not the site-wide average). Ties go to the program with more reviews, then the newer one.
- **For the directory:** `IProgramCatalog.GetPublishingTrainersAsync` returns each trainer with published programs, with their program count and the sums of their ratings.

### Trainers
- `GET /api/v1/trainers?search=&pageIndex=&pageSize=` lists trainer profiles that have published programs.
- **Search** covers name, bio and specialties.
- **Order:** most followers first, then by name.
- **Each card** shows the avatar, specialties, follower count, program count and rating.

### Angular
- **Routes:** `/` becomes a **landing page**: a hero, top-rated programs, new programs, featured trainers and how the site works. The catalog moves to **`/programs`**.
- **Catalog:**
  - search, pricing type, kind and sort controls
  - Previous/Next paging
  - every setting is kept in the query string, so the landing page can link to `/programs?sort=TopRated`
- **Shared card:** a `ProgramCardComponent` is used by the catalog and the landing page.
- **Trainer directory** at **`/trainers`**, with a "Trainers" link in the nav.

## Tests
- **Functional:**
  - search by summary and by trainer name
  - the pricing filter
  - price sort
  - top-rated order: a new program with a single 5-star review ranks below an established 4.8
  - paging
  - the directory lists only trainers who publish, and its search works
- **Unit:** the price copy follows the pricing changes.
- **Angular:** the query-string mapping and the service parameters.

## As built

- **Built on branch `discovery`** (from `main` after PR #9), with one change from the Q&A:
  - **The top-rated prior is a neutral 3 stars, not the site-wide average.** With the site-wide average as the prior, a single 5-star review would still outrank fifty 4.8s while most programs score high, which defeats the point.
  - With `C = 5`, fifty reviews averaging 4.8 score 4.64, and one 5-star review scores 3.33.
- **Programs:**
  - migration `AddCatalogPrice` adds `PriceModel` and `PriceAmountMinor`, and backfills them from each program's latest price version
  - `IProgramCatalog.GetPublishingTrainersAsync` feeds the directory
- **Trainers:**
  - `ITrainerDirectory` is implemented by `ProfileDirectory`
  - new `ListTrainers` slice
- **Angular:**
  - `ProgramCardComponent` and `TrainerCardComponent` are shared by the catalog, the directory and the landing page
  - catalog state goes through `readCatalogState` / `catalogQueryParams`, and typing in search replaces the history entry instead of adding one
- **Tests:**
  - 5 functional (`DiscoveryApiTests`)
  - 1 unit (the price copy)
  - 6 Angular specs
- **End-to-end run in the AppHost:**
  - The landing page showed the hero, new programs, trainers and how it works. Top rated was hidden because no program had a visible review.
  - `/programs?q=Jordan&sort=TopRated` found both of Jordan's programs by trainer name and set the controls from the URL.
  - Picking Subscription updated the URL and showed "Clear the filters"; clearing worked.
  - `/trainers` listed Jordan Steel (2 programs). A search for "mobil" matched and "yoga" didn't.
- **Not checked by eye:** the Top rated row with real ratings. The ordering is covered by the functional test.

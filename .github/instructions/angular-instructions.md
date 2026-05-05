# Angular Coding Standards & Instructions

Follow these rules for all Angular-related code suggestions and refactors:

### 1. Component Architecture
- Use **Standalone Components** by default (Angular 17+).
- Prefer **Signal-based** state management over traditional variables.
- Use the `inject()` function for Dependency Injection instead of constructor injection.
- Always use `changeDetection: ChangeDetectionStrategy.OnPush`.

### 2. Templates & Styling
- Use the **New Control Flow** syntax (`@if`, `@for`, `@switch`) instead of `*ngIf` or `*ngFor`.
- Use **Tailwind CSS** utility classes for styling. Avoid inline styles or large CSS files.
- Apply the `async` pipe for any Observables used in templates.

### 3. State & Logic
- Use **Signals** for local UI state (`signal`, `computed`, `effect`).
- Use **RxJS** only for asynchronous data streams (e.g., HTTP requests).
- Convert Observables to Signals using `toSignal()` when displaying data in templates.

### 4. Naming Conventions
- Components: `name.component.ts`
- Services: `name.service.ts`
- Interfaces: `name.model.ts` (Prefer `interface` over `class` for data models).

### 5. Testing
- Write unit tests using **Jasmine and Karma**.
- Focus on testing logic within Signals and Services rather than DOM rendering.

### 6. RxJS Best Practices
- **Prefer `toSignal()`**: Convert HTTP Observables to Signals in components to simplify the template and lifecycle management.
- **Avoid Manual Subscriptions**: Never use `.subscribe()` inside components; use the `async` pipe or `toSignal()` instead.
- **Pipeable Operators**: Use `pipe()` with `map`, `filter`, and `catchError`. Avoid complex nesting.
- **Memory Management**: If a manual subscription is unavoidable, always clean it up using `takeUntilDestroyed()`.
- **Error Handling**: Use `catchError` in every data stream to return a safe fallback or trigger a notification service.

### 7. API Integration Rules
- **Service-Based**: All HTTP calls must reside in a dedicated `Service`. Components should never call `HttpClient` directly.
- **Strong Typing**: Every API response must have a corresponding `interface`. Avoid using `any`.
- **Environment Constants**: Use `environment.ts` for Base URLs and API keys.
- **Interceptors**: Use Functional Interceptors for adding Auth tokens or global error handling.
- **Clean Requests**: Use `HttpParams` for query strings and ensure all POST bodies match the backend interface exactly.
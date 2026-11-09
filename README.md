# Gym Hub

Welcome to the **Gym Hub**! Trainers build workout programs and grow a following; users subscribe to programs, train through them week by week, and talk with the trainer and other subscribers on each program's discussion board.

**Table of Contents**  
[Features](#features)  
[Technologies](#technologies)  
[Architecture](#architecture)  
[Getting Started](#getting-started)  
[Usage](#usage)  
[Contributing](#contributing)  
[License](#license)  

## Features
- Trainers author structured programs: weeks → days → workouts → exercises, with notes, images and video
- Fixed-date or ongoing programs, with each week released on a calendar date
- Free, one-time or recurring pricing set by the trainer *(paid checkout with Stripe Connect is planned)*
- Subscriber-only discussion board per program *(planned)*
- Workout logging and progress tracking *(planned)*
- Follow trainers, notifications, reviews and moderation *(planned)*

## Technologies
- **.NET 10** with ASP.NET Core Minimal APIs
- **.NET Aspire** for local orchestration, service discovery and telemetry
- **PostgreSQL** with one schema per module (EF Core)
- **Keycloak** for identity, with the dev realm checked in as code
- **YARP** Backend-for-Frontend (BFF) gateway
- **S3-compatible object storage** for media (RustFS locally)
- **Angular 21** with signals, Tailwind CSS and Vitest

## Architecture
GymHub is a **modular monolith with vertical slices**:

- `GymHub.Api` hosts independent modules (currently **Programs** and **Subscriptions**). Each module owns its data in its own Postgres schema and exposes only a `.Contracts` project to other modules.
- Inside a module, each feature is a vertical slice: endpoint, request/response, validation and handler together.
- Modules talk through contract interfaces for reads and through integration events delivered in-process from a transactional outbox.
- The **Gateway** is a BFF. It signs users in with Keycloak, keeps tokens on the server, serves the Angular app on the same origin, and forwards `/gh-api/**` calls to the API with the user's access token and XSRF protection.

See [.github/instructions/dotnet-instructions.md](.github/instructions/dotnet-instructions.md) for the conventions.

## Getting Started
**Prerequisites**  
- .NET 10 SDK  
- Docker  
- Node.js (npm)  

**Run it**  
1. Clone the repository and install the web app's packages:
   ```bash
   git clone https://github.com/MattRathburn/GymHub.git
   cd GymHub/src/GH.WebApp/gh.webapp.ng && npm install && cd ../../..
   ```
2. Set the local secrets the AppHost expects (any values you like):
   ```bash
   dotnet user-secrets set "Parameters:bff-client-secret" "<random>" --project src/GymHub.AppHost
   dotnet user-secrets set "Parameters:media-access-key" "<random>" --project src/GymHub.AppHost
   dotnet user-secrets set "Parameters:media-secret-key" "<random>" --project src/GymHub.AppHost
   ```
3. Start everything. Aspire starts Postgres, Keycloak, object storage, the migrations, the API, the Gateway and the Angular app:
   ```bash
   dotnet run --project src/GymHub.AppHost
   ```
4. Open the Gateway at https://localhost:7179. The dev realm seeds `member`, `trainer` and `admin` users; their passwords are in `src/GymHub.AppHost/Realms/GH-Test-Realm-realm.json`.

**Tests**  
```bash
dotnet test GymHub.slnx
```
The functional tests need Docker. For the Angular tests, run `npm test` in `src/GH.WebApp/gh.webapp.ng`.

## Usage
Sign in as `trainer`, open **Trainer studio**, create a program, then add weeks, days and workouts and publish it. Sign in as `member` to find the program, subscribe, and see the weeks released so far.

## Contributing
We welcome contributions! Please read our Contributing Guidelines for more details.

## License
This project is licensed under the MIT License. See the LICENSE file for details.

# Gym Hub

Welcome to the **Gym Hub**! This application allows users to follow coaches and subscribe to their various workout programs. Or for coaches to build a following by creating programs. Built with modern technologies and architectures, it ensures a robust and scalable platform for fitness enthusiasts.

**Table of Contents**  
[Features](#features)  
[Technologies](#technologies)  
[Architecture](#architecure)  
[Getting Started](#getting-started)  
[Usage](#usage)  
[Contributing](#contributing)  
[License](#license)  

## Features
- Follow fitness coaches and trainers  
- Subscribe to different workout programs  
- Track progress and achievements  
- Community forums and discussions  
- Personalized workout recommendations    

## Technologies
.NET 8: Core framework for building the application  
Docker: Containerization for consistent development and deployment environments  
PostgreSQL: Primary database for storing user and workout data  
SQL Server: Secondary database for specific use cases  
Identity Server: Authentication and authorization  
Mass Transit: Message-based communication between microservices  

## Architecture
The application is built using Clean Architecture and Vertical Slice Architecture to ensure maintainability and scalability.

**Clean Architecture**  
Domain Layer: Contains business logic and entities  
Application Layer: Contains use cases and service interfaces  
Infrastructure Layer: Contains implementations for database access, messaging, etc.  
Presentation Layer: Contains API controllers and UI components  

**Vertical Slice Architecture**  
Each feature is implemented as a vertical slice, containing all necessary components (e.g., controllers, services, repositories) in a single module.  

## Getting Started
**Prerequisites**  
.NET 8 SDK  
Docker  
PostgreSQL  
SQL Server  

**Installation**  
Clone the repository:
git clone https://github.com/MattRathburn/GymHub.git  
cd GymHub

Set up Docker containers:
docker-compose up -d

Apply database migrations:
dotnet ef database update --project src/Infrastructure

Run the application:
dotnet run --project src/Presentation

## Usage
Register and log in to the application.
Follow coaches and subscribe to workout programs.
Track your progress and participate in community discussions.

## Contributing
We welcome contributions! Please read our Contributing Guidelines for more details.

## License
This project is licensed under the MIT License. See the LICENSE file for details.
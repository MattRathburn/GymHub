## API Endpoint Template
- Use MediatR
- No business logic in controllers
- Validate inputs with FluentValidation
- Return Result<T> pattern

## Handler Template
- Must be async
- Inject only required dependencies
- Include logging
- Handle nulls explicitly
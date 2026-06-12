# Agent: scaffold-solution

Bootstrap a brand-new solution from scratch with the full stack: Domain → Application → Infrastructure → Api.

**Trigger:** "scaffold new solution" / "create new project" / "bootstrap solution {SolutionName}"

---

## Inputs required
- **Solution name** (PascalCase): e.g. `MyApp`
- **Target folder**: default = current working directory
- **Features to seed**: list of entity names to pre-generate (optional)

---

## Step 1 — Load all skills

Read ALL skill files:
- `.claude/skills/dotnet-project-scaffold/skill.md`
- `.claude/skills/domain-layer/skill.md`
- `.claude/skills/application-cqrs/skill.md`
- `.claude/skills/infrastructure-layer/skill.md`
- `.claude/skills/webapi-layer/skill.md`
- `.claude/skills/ef-dapper-setup/skill.md`
- `.claude/skills/di-scrutor/skill.md`
- `.claude/skills/validation-fluent/skill.md`

---

## Step 2 — Scaffold solution shell

```bash
mkdir {SolutionName} && cd {SolutionName}
dotnet new sln -n {SolutionName}

dotnet new classlib -n {SolutionName}.Domain      -f net8.0
dotnet new classlib -n {SolutionName}.Application -f net8.0
dotnet new classlib -n {SolutionName}.Infrastructure -f net8.0
dotnet new webapi   -n {SolutionName}.Api         -f net8.0

dotnet sln add {SolutionName}.Domain/{SolutionName}.Domain.csproj
dotnet sln add {SolutionName}.Application/{SolutionName}.Application.csproj
dotnet sln add {SolutionName}.Infrastructure/{SolutionName}.Infrastructure.csproj
dotnet sln add {SolutionName}.Api/{SolutionName}.Api.csproj

dotnet add {SolutionName}.Application reference {SolutionName}.Domain
dotnet add {SolutionName}.Infrastructure reference {SolutionName}.Application
dotnet add {SolutionName}.Infrastructure reference {SolutionName}.Domain
dotnet add {SolutionName}.Api reference {SolutionName}.Application
dotnet add {SolutionName}.Api reference {SolutionName}.Infrastructure
```

---

## Step 3 — Add NuGet packages

```bash
# Domain
dotnet add {SolutionName}.Domain package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.4
dotnet add {SolutionName}.Domain package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.23
dotnet add {SolutionName}.Domain package System.IdentityModel.Tokens.Jwt --version 8.15.0

# Application
dotnet add {SolutionName}.Application package MediatR --version 12.4.1
dotnet add {SolutionName}.Application package FluentValidation --version 11.9.0
dotnet add {SolutionName}.Application package FluentValidation.DependencyInjectionExtensions --version 11.9.0
dotnet add {SolutionName}.Application package Mapster --version 7.4.0

# Infrastructure
dotnet add {SolutionName}.Infrastructure package Microsoft.EntityFrameworkCore --version 8.0.13
dotnet add {SolutionName}.Infrastructure package Microsoft.EntityFrameworkCore.Proxies --version 8.0.13
dotnet add {SolutionName}.Infrastructure package Pomelo.EntityFrameworkCore.MySql --version 8.0.3
dotnet add {SolutionName}.Infrastructure package Microsoft.Extensions.DependencyInjection --version 8.0.1
dotnet add {SolutionName}.Infrastructure package Microsoft.Extensions.Hosting --version 8.0.0
dotnet add {SolutionName}.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions --version 8.0.0

# Api
dotnet add {SolutionName}.Api package Microsoft.EntityFrameworkCore.Tools --version 8.0.4
dotnet add {SolutionName}.Api package Swashbuckle.AspNetCore --version 6.6.2
```

---

## Step 4 — Create folder structure

Create these directories (write placeholder `.gitkeep` if needed):
```
{SolutionName}.Domain/
  Abstractions/Entities/
  Abstractions/Repositories/
  Abstractions/Events/
  Abstractions/ValueObjects/
  Entities/
  Entities/Outbox/
  Events/
  Exceptions/
  ValueObjects/

{SolutionName}.Application/
  Abstractions/Events/
  Behaviors/
  CQRS/
  DI/
  Features/

{SolutionName}.Infrastructure/
  Interceptors/
  Migrations/
  Outbox/
  Repositories/
  DI/

{SolutionName}.Api/
  Controllers/
  DI/
  Middlewares/
  Models/
```

---

## Step 5 — Write domain abstractions

Using the exact code from `.claude/skills/domain-layer/skill.md`, write:
- `Abstractions/Entities/Entity.cs`
- `Abstractions/Entities/AggregateRoot.cs`
- `Abstractions/Entities/IAuditableEntity.cs`
- `Abstractions/Entities/ISoftDeleteEntity.cs`
- `Abstractions/Entities/PagedResult.cs`
- `Abstractions/Repositories/IEFRepository.cs`
- `Abstractions/Events/IDomainEvent.cs`
- `Abstractions/ValueObjects/ValueObject.cs`
- `Abstractions/IUnitOfWork.cs`
- `Entities/Outbox/OutboxMessage.cs`
- `Exceptions/DomainException.cs`

```csharp
// DomainException.cs
namespace {SolutionName}.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
```

Also write identity entities:
```csharp
// Entities/AppUser.cs
using Microsoft.AspNetCore.Identity;
namespace {SolutionName}.Domain.Entities;
public class AppUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTimeOffset? BirthDay { get; set; }
}

// Entities/AppRole.cs
using Microsoft.AspNetCore.Identity;
namespace {SolutionName}.Domain.Entities;
public class AppRole : IdentityRole
{
    public string? Description { get; set; }
}
```

---

## Step 6 — Write infrastructure layer

Using `.claude/skills/infrastructure-layer/skill.md`:
- `Repositories/EFRepository.cs`
- `EFUnitOfWork.cs`
- `ApplicationDbContext.cs` (initial — includes OutboxMessages DbSet)
- `Interceptors/AuditableEntitiesInterceptor.cs`
- `Outbox/OutboxMessageProcessor.cs`
- `DI/ServiceCollectionExtensions.cs`
- `DI/Options.cs`

---

## Step 7 — Write application layer

- `CQRS/ICommand.cs`, `CQRS/IQuery.cs`, `CQRS/ICommandHandler.cs`, `CQRS/IQueryHandler.cs`
- `Abstractions/Events/DomainEventWrapper.cs`
- `Behaviors/LoggingBehavior.cs`, `Behaviors/ValidationBehavior.cs`, `Behaviors/TransactionBehavior.cs`
- `DI/ServiceCollectionExtensions.cs` (registers MediatR + behaviors)
- `AssemblyReference.cs`

---

## Step 8 — Write API layer

Using `.claude/skills/webapi-layer/skill.md`:
- `Models/ApiResponse.cs`
- `Middlewares/ExceptionHandlingMiddleware.cs`
- `DI/SwaggerExtension.cs`
- `Program.cs` (full wired-up startup)

Note: Do NOT create `FluentValidationMiddleware.cs` — validation is handled by `ValidationBehavior` in the MediatR pipeline.

---

## Step 9 — Write appsettings.json

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost,3306;database={solutionname};User=root;Password="
  },
  "MariaDbRetryOptions": { "MaxRetryCount": 5, "MaxRetryDelay": "00:00:05", "ErrorNumbersToAdd": [] },
  "Jwt": {
    "Key": "CHANGE_THIS_SECRET_KEY_MIN_32_CHARS_LONG",
    "Issuer": "{SolutionName}.AuthServer",
    "Audience": "{SolutionName}.Clients",
    "AccessTokenExpirationMinutes": 60
  }
}
```

---

## Step 10 — Build and verify

```bash
dotnet build {SolutionName}.sln
```

If build passes: print the full folder tree and a checklist of what was created.
If build fails: list errors, do NOT auto-fix silently. Ask user.

---

## Step 11 — Seed features (if requested)

For each entity name provided in inputs, run the `add-feature` agent instructions for that entity.

---

## Step 12 — Initial migration

```bash
dotnet ef migrations add Init --project {SolutionName}.Infrastructure --startup-project {SolutionName}.Api
dotnet ef database update --project {SolutionName}.Infrastructure --startup-project {SolutionName}.Api
```

Print connection string reminder and confirm MariaDB must be running at `localhost:3306`.

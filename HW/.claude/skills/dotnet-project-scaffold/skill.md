# Skill: dotnet-project-scaffold

Canonical .csproj content, folder structure, and CLI commands for this solution.

---

## Project dependency chain
```
HW.Api → HW.Infrastructure → HW.Application → HW.Domain
HW.Api → HW.Application
```

## HW.Domain.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="8.0.4" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.23" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.15.0" />
  </ItemGroup>
</Project>
```

## HW.Application.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentValidation" Version="11.9.0" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.9.0" />
    <PackageReference Include="Mapster" Version="7.4.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HW.Domain\HW.Domain.csproj" />
  </ItemGroup>
</Project>
```

## HW.Infrastructure.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.13" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Proxies" Version="8.0.13" />
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.3" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HW.Application\HW.Application.csproj" />
    <ProjectReference Include="..\HW.Domain\HW.Domain.csproj" />
  </ItemGroup>
</Project>
```

## HW.Api.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HW.Application\HW.Application.csproj" />
    <ProjectReference Include="..\HW.Infrastructure\HW.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

## Folder layout per project

```
HW.Domain/
├── Abstractions/
│   ├── Entities/        Entity.cs, IAuditableEntity.cs, ISoftDeleteEntity.cs, PagedResult.cs
│   ├── Repositories/    IEFRepository.cs, IRepository.cs
│   └── IUnitOfWork.cs
├── Entities/            AppUser.cs, AppRole.cs, Blog.cs, MasterData.cs, ...
└── Exceptions/          DomainException.cs

HW.Application/
├── Common/
│   └── Mapping/         {Feature}MappingConfig.cs
├── Dtos/
│   └── {Feature}/       {Feature}ResponseDto.cs, Create{Feature}RequestDto.cs, Update{Feature}RequestDto.cs
├── Services/            I{Feature}Service + {Feature}Service (same file)
├── Validators/          {Feature}Validator.cs
└── DI/                  ServiceCollectionExtensions.cs

HW.Infrastructure/
├── Interceptors/        AuditableEntitiesInterceptor.cs
├── Migrations/          (EF generated)
├── MultiTenant/         UserInfo.cs
├── Repositories/        EFRepository.cs, EFUnitOfWork.cs
├── DI/                  ServiceCollectionExtensions.cs, Options.cs
└── ApplicationDbContext.cs

HW.Api/
├── Controllers/         {Feature}Controller.cs
├── DI/                  SwaggerServiceCollectionExtensions.cs
├── Middlewares/         ExceptionHandlingMiddleware.cs, FluentValidationMiddleware.cs
├── Models/              ApiResponse.cs (ApiResponseFactory)
├── Properties/          launchSettings.json
├── appsettings.json
└── Program.cs
```

## CLI commands (run from solution root)

```bash
# Build
dotnet build HW.slnx

# Run API
dotnet run --project HW.Api

# EF Core migration
dotnet ef migrations add <Name> --project HW.Infrastructure --startup-project HW.Api
dotnet ef database update --project HW.Infrastructure --startup-project HW.Api
dotnet ef migrations remove --project HW.Infrastructure --startup-project HW.Api
dotnet ef migrations list --project HW.Infrastructure --startup-project HW.Api

# Add package to a project
dotnet add HW.Infrastructure package <PackageName>
```

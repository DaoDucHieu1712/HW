# Built from the solution folder:  docker build -f HW.Api/Dockerfile .
# (docker-compose.yml does this for you under the "app" profile.)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first, so the restore layer is reused until a package reference changes.
COPY HW.Api/HW.Api.csproj                       HW.Api/
COPY HW.Application/HW.Application.csproj       HW.Application/
COPY HW.Infrastructure/HW.Infrastructure.csproj HW.Infrastructure/
COPY HW.Domain/HW.Domain.csproj                 HW.Domain/
COPY HW.Agentic/HW.Agentic.csproj               HW.Agentic/
RUN dotnet restore HW.Api/HW.Api.csproj

COPY HW.Api/            HW.Api/
COPY HW.Application/    HW.Application/
COPY HW.Infrastructure/ HW.Infrastructure/
COPY HW.Domain/         HW.Domain/
COPY HW.Agentic/        HW.Agentic/
RUN dotnet publish HW.Api/HW.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# The Serilog file sink writes logs/ relative to the working directory, which the non-root user
# cannot create under /app on its own.
RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HW.Api.dll"]

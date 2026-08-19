FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Authentication.sln .
COPY src/Authentication.Domain/Authentication.Domain.csproj src/Authentication.Domain/
COPY src/Authentication.Application/Authentication.Application.csproj src/Authentication.Application/
COPY src/Authentication.Infrastructure/Authentication.Infrastructure.csproj src/Authentication.Infrastructure/
COPY src/Authentication.Api/Authentication.Api.csproj src/Authentication.Api/
COPY tests/Authentication.Tests/Authentication.Tests.csproj tests/Authentication.Tests/
RUN dotnet restore src/Authentication.Api/Authentication.Api.csproj

COPY . .
RUN dotnet publish src/Authentication.Api/Authentication.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user - never run the API process as root in a container.
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Authentication.Api.dll"]

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for layer caching
COPY src/AiCon.Api/AiCon.Api.csproj src/AiCon.Api/
COPY src/AiCon.ServiceDefaults/AiCon.ServiceDefaults.csproj src/AiCon.ServiceDefaults/

# Restore dependencies
RUN dotnet restore src/AiCon.Api/AiCon.Api.csproj

# Copy source code and publish
COPY src/AiCon.Api/ src/AiCon.Api/
COPY src/AiCon.ServiceDefaults/ src/AiCon.ServiceDefaults/

RUN dotnet publish src/AiCon.Api/AiCon.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AiCon.Api.dll"]

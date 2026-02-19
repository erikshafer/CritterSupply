# Multi-stage build for Fulfillment.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files for package management
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy entire src directory (tests excluded via .dockerignore)
COPY src/ src/

# Restore dependencies
RUN dotnet restore "src/Fulfillment/Fulfillment.Api/Fulfillment.Api.csproj"

# Build
WORKDIR /src/src/Fulfillment/Fulfillment.Api
RUN dotnet build Fulfillment.Api.csproj -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish Fulfillment.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Fulfillment.Api.dll"]

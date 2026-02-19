# Multi-stage build for Orders.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files for package management
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy entire src directory (tests excluded via .dockerignore)
COPY src/ src/

# Restore dependencies
RUN dotnet restore "src/Orders/Orders.Api/Orders.Api.csproj"

# Build
WORKDIR /src/src/Orders/Orders.Api
RUN dotnet build Orders.Api.csproj -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish Orders.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Orders.Api.dll"]

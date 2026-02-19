# Multi-stage build for Storefront.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files for package management
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy entire src directory to simplify path handling with spaces
COPY src/ src/

# Restore dependencies
RUN dotnet restore "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"

# Build
WORKDIR "/src/src/Customer Experience/Storefront.Api"
RUN dotnet build Storefront.Api.csproj -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish Storefront.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Storefront.Api.dll"]

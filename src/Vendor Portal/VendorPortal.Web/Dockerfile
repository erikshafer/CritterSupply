# Multi-stage build for VendorPortal.Web (Blazor WebAssembly)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files for package management
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy entire src directory to simplify path handling with spaces
COPY src/ src/

# Publish (produces static wwwroot/ files)
RUN dotnet publish "src/Vendor Portal/VendorPortal.Web/VendorPortal.Web.csproj" \
    -c Release -o /app/publish

# Serve stage - nginx for static file hosting
FROM nginx:alpine AS final
EXPOSE 8080
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY ["src/Vendor Portal/VendorPortal.Web/nginx.conf", "/etc/nginx/conf.d/default.conf"]

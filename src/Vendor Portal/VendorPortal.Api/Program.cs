using System.Text;
using JasperFx;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using VendorPortal.Api.Hubs;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.ApplyJasperFxExtensions();

// JWT Bearer authentication (validates tokens issued by VendorIdentity.Api)
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new Exception("Jwt:SigningKey not found in configuration");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "vendor-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "vendor-portal";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Support JWT from query string for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/vendor-portal"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// CORS for VendorPortal.Web (Blazor WASM at port 5241)
builder.Services.AddCors(options =>
{
    options.AddPolicy("VendorPortalWeb", policy =>
    {
        policy.WithOrigins("http://localhost:5241")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// SignalR
builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.DisableConventionalDiscovery();
    opts.Discovery.IncludeAssembly(typeof(VendorPortal.Api.Dashboard.GetDashboardEndpoint).Assembly);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseCors("VendorPortalWeb");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options => { options.RouteTemplate = "api/{documentName}/swagger.json"; });
    app.UseSwaggerUI(opts =>
    {
        opts.RoutePrefix = "api";
        opts.SwaggerEndpoint("/api/v1/swagger.json", "Vendor Portal API");
    });
}

app.MapDefaultEndpoints();

app.MapWolverineEndpoints(opts =>
{
    opts.UseFluentValidationProblemDetailMiddleware();
});

app.MapHub<VendorPortalHub>("/hub/vendor-portal");

app.MapGet("/", (HttpResponse response) =>
{
    response.Headers.Append("Location", "/api");
    response.StatusCode = StatusCodes.Status301MovedPermanently;
}).ExcludeFromDescription();

return await app.RunJasperFxCommands(args);

public partial class Program { }

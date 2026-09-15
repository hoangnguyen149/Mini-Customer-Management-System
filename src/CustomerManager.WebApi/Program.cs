using System.Text;
using System.Threading.RateLimiting;
using CustomerManager.Application;
using CustomerManager.Infrastructure;
using CustomerManager.WebApi.ExceptionHandling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Application + Infrastructure (Services, FluentValidation, EF Core, JWT, seeder)
// ---------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// Controllers + ProblemDetails (RFC 7807) — see GlobalExceptionHandler for the
// single place every exception is turned into a problem+json response.
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------------------------------------------------------------------------
// JWT Authentication. Secret is required (min 32 chars / 256 bits) and is never
// read from appsettings.json — see README.md "Setup" for User Secrets /
// environment variable configuration.
// ---------------------------------------------------------------------------
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or shorter than 32 characters. Configure it with " +
        "'dotnet user-secrets set \"Jwt:Secret\" \"<a long random string>\"' (dev) " +
        "or an environment variable (prod) before starting the API — see README.md.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "CustomerManager";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "CustomerManager.Client";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting: a strict Token Bucket policy on /api/auth/login only. There is
// exactly one admin account in this system, so brute-force risk is higher than
// on a normal multi-user login — see Phase 1 design doc section 9.3.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// ---------------------------------------------------------------------------
// Output Caching for GET /api/customers. Redis is optional/pluggable: if
// ConnectionStrings:Redis is configured, cached entries are shared across
// instances; otherwise this falls back to the built-in in-memory store so the
// Mini Project runs with zero extra infrastructure. Either way, every write
// endpoint evicts the "customers" tag (see CustomersController) — an admin must
// never see stale data after their own edit.
// ---------------------------------------------------------------------------
void ConfigureOutputCache(Microsoft.AspNetCore.OutputCaching.OutputCacheOptions options)
{
    options.AddPolicy("CustomersListPolicy", policy => policy
        .Expire(TimeSpan.FromSeconds(30))
        .Tag("customers")
        .SetVaryByQuery("fullName", "phoneNumber", "isActive", "pageNumber", "pageSize", "sortBy", "sortDirection"));
}

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisOutputCache(options =>
    {
        ConfigureOutputCache(options);
        options.Configuration = redisConnectionString;
        options.InstanceName = "CustomerManager:";
    });
}
else
{
    builder.Services.AddOutputCache(ConfigureOutputCache);
}

// ---------------------------------------------------------------------------
// CORS — only the configured Blazor client origin(s), never AllowAnyOrigin.
// ---------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ---------------------------------------------------------------------------
// Swagger / OpenAPI (dev only — see app.Environment.IsDevelopment() below).
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "CustomerManager API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler(); // delegates to GlobalExceptionHandler for every environment — no leaked stack traces even in dev.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("BlazorClient");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program
{
}

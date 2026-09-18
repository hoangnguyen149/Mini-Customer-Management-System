using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using CustomerManager.Application;
using CustomerManager.Infrastructure;
using CustomerManager.WebApi.ExceptionHandling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Don't announce "Kestrel" to every caller — a minor bit of stack-fingerprinting
// hardening (OWASP: don't leak technology details in headers).
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------------------------------------------------------------------------
// Forwarded headers (X-Forwarded-For/-Proto) for when this API sits behind a
// reverse proxy/load balancer. Only trusted once at least one proxy IP is
// explicitly configured — trusting these headers unconditionally would let any
// client spoof its own IP (defeats the login rate limiter below) and scheme
// (Issue M7). Configure "ForwardedHeaders:KnownProxies" in production.
// ---------------------------------------------------------------------------
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies)
    {
        if (IPAddress.TryParse(proxy, out var ip))
        {
            options.KnownProxies.Add(ip);
        }
    }

    options.ForwardedHeaders = knownProxies.Length > 0
        ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        : ForwardedHeaders.None;
});

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
// CORS — only the configured Blazor client origin(s), never AllowAnyOrigin.
// ---------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
        .WithHeaders("Content-Type", "Authorization"));
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

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(180);
    options.IncludeSubDomains = true;
});

// ---------------------------------------------------------------------------
// Customer import (Excel/CSV) upload size — enforced at the multipart-parsing
// level (before CustomerImportController/CustomerImportService ever see the
// request), in addition to the same limit re-checked in
// CustomerImportService.PreviewAsync from the same config key. Read here, not
// hard-coded, per Import:MaxFileSizeBytes in appsettings.json.
// ---------------------------------------------------------------------------
var maxImportFileSizeBytes = builder.Configuration.GetValue<long?>("Import:MaxFileSizeBytes") ?? 5 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxImportFileSizeBytes;
});

var app = builder.Build();

app.UseForwardedHeaders(); // must run before anything that reads RemoteIpAddress/scheme (rate limiter, HTTPS redirection).

app.UseExceptionHandler(); // delegates to GlobalExceptionHandler for every environment — no leaked stack traces even in dev.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // HSTS only makes sense once you're actually terminating HTTPS in front of
    // this app (a real cert) — forcing it in local dev would just break http
    // debugging against localhost.
    app.UseHsts();
}

app.UseHttpsRedirection();

// ---------------------------------------------------------------------------
// Security response headers — defense-in-depth against clickjacking, MIME
// sniffing, and (for the handful of HTML responses this API ever serves, e.g.
// Swagger in dev) inline-script injection. This is a JSON API with no
// user-rendered HTML of its own, so the CSP is deliberately locked down to
// "nothing" rather than trying to allowlist a UI it doesn't have.
// ---------------------------------------------------------------------------
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.Remove("X-Powered-By");

    // Skipped in Development: Swagger UI (dev-only, see above) needs to run its
    // own inline scripts/styles, which a locked-down CSP would block.
    if (!app.Environment.IsDevelopment())
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }

    await next();
});

app.UseCors("BlazorClient");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program
{
}

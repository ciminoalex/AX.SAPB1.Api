using AX.SAPB1.Api.Authentication;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Support;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service hosting and ensure working directory is the app base
builder.Host.UseWindowsService();
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Configure logging to console with scopes and detailed levels
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.IncludeScopes = true;
});
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "AX SAP B1 API", Version = "v1" });

    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter 'Bearer {token}'",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { securityScheme, new List<string>() }
    });
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register custom services
builder.Services.AddScoped<IDbOdbcService, DbOdbcService>();
builder.Services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();

// Provisioning all'avvio dei campi utente AX.360 sui documenti di marketing SAP B1.
builder.Services.AddHostedService<Ax360UserFieldsBootstrapService>();

// HttpClient for SAP B1 auth probing (login validation)
builder.Services.AddHttpClient<ISapB1AuthService, SapB1AuthService>()
    .ConfigureHttpClient((provider, client) =>
    {
        var cfg = provider.GetRequiredService<IConfiguration>();
        var baseUrl = cfg["SapB1:ServiceLayerUrl"] ?? throw new ArgumentNullException("SapB1:ServiceLayerUrl");
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(100);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
    })
    .AddHttpMessageHandler(provider =>
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        return new LoggingHttpMessageHandler(loggerFactory.CreateLogger<LoggingHttpMessageHandler>());
    });

// Register HttpClient for SAP B1 Service Layer with SSL bypass
builder.Services.AddHttpClient<ISapB1ServiceLayerService, SapB1ServiceLayerService>()
    .ConfigureHttpClient(client =>
    {
        // Shorten timeout visibility and ensure we log when exceeded via handler
        client.Timeout = TimeSpan.FromSeconds(100);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
            {
                // Ignora tutti i problemi di certificati SSL
                Console.WriteLine($"SSL Certificate validation bypassed. Policy errors: {sslPolicyErrors}");
                return true;
            }
        };
    })
    .AddHttpMessageHandler(provider =>
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        return new LoggingHttpMessageHandler(loggerFactory.CreateLogger<LoggingHttpMessageHandler>());
    });

// Configure Authentication before building the app
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Configurazione JWT mancante: impostare 'Jwt:Key' in appsettings o variabili d'ambiente.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrEmpty(jwtSection["Issuer"]),
            ValidateAudience = !string.IsNullOrEmpty(jwtSection["Audience"]),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    })
    // Schema a chiave API per il portale AX.360 (machine-to-machine, header X-Api-Key).
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

// Policy combinata: un endpoint è accessibile con JWT Bearer valido OPPURE con una chiave API valida.
// Impostata come FallbackPolicy così da proteggere tutti gli endpoint privi di [Authorize]
// (gli endpoint pubblici restano marcati [AllowAnonymous], es. login/test).
builder.Services.AddAuthorization(options =>
{
    var jwtOrApiKey = new AuthorizationPolicyBuilder(
            JwtBearerDefaults.AuthenticationScheme,
            ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();

    options.DefaultPolicy = jwtOrApiKey;
    options.FallbackPolicy = jwtOrApiKey;
});

var app = builder.Build();

// Configure the HTTP request pipeline (Swagger sempre attivo)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AX SAP B1 API v1");
});
app.MapOpenApi();

// Redirect to HTTPS only in Development (service typically runs HTTP unless cert configured)
if (builder.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Enable CORS
app.UseCors("AllowAngularApp");

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Request/response timing middleware for all incoming requests
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestTiming");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        logger.LogInformation("HTTP {Method} {Path} started", context.Request.Method, context.Request.Path);
        await next();
        sw.Stop();
        logger.LogInformation("HTTP {Method} {Path} completed in {ElapsedMs} ms with {StatusCode}", context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds, context.Response.StatusCode);
    }
    catch (Exception ex)
    {
        sw.Stop();
        logger.LogError(ex, "HTTP {Method} {Path} failed after {ElapsedMs} ms", context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        throw;
    }
});

app.MapControllers();

app.Run();

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AX.SAPB1.Api.Authentication;
using AX.SAPB1.Api.Companies;
using AX.SAPB1.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Un solo servizio per più company SAP (MTF, HTDI…): la company la decide il login, e l'errore che questi test
/// esistono per impedire è uno solo — un client di un'azienda che legge o scrive lo schema di un'altra.
/// </summary>
public class MultiCompanyTests
{
    // ── Registro ──

    [Fact]
    public void La_configurazione_storica_resta_una_sola_company()
    {
        var registry = TestCompanies.Registry(new Dictionary<string, string?> { ["Auth:ApiKeys:0"] = "chiave-mtf" });

        var only = Assert.Single(registry.Companies);
        Assert.Equal("default", only.Id);
        Assert.Equal("SBO_MTF", only.CompanyDB);
        Assert.True(only.IsPrimary);
        Assert.False(only.ReadOnly);
        Assert.Same(only, registry.FindByApiKey("chiave-mtf"));
    }

    [Fact]
    public void La_chiave_api_sceglie_la_company()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());

        Assert.Equal("SBO_MTF", registry.FindByApiKey("chiave-mtf")!.CompanyDB);
        Assert.Equal("SBOHITECH_PROD", registry.FindByApiKey("chiave-htdi")!.CompanyDB);
        Assert.Null(registry.FindByApiKey("chiave-sbagliata"));
        Assert.Null(registry.FindByApiKey(""));
        Assert.True(registry.TryGet("HTDI", out var htdi));
        Assert.Equal("htdi", htdi.Id);
    }

    [Fact]
    public void Una_company_aggiuntiva_non_eredita_interruttori_ne_dati_della_principale()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi(new Dictionary<string, string?>
        {
            ["SapB1:Write:Enabled"] = "true",
            ["SapB1:Bootstrap:UserFields:Enabled"] = "true",
            ["SapB1:SalesDocuments:AllowPostedInvoices"] = "true",
            ["SapB1:TimeAndMaterialsItemCode"] = "TM",
            ["SapB1:FiscalProjectCodePattern"] = "PRJ{yy}_{yy}{seq:5}",
            ["SapB1:UserName"] = "manager",
            ["SapB1:SessionTimeoutMinutes"] = "20",
        }));
        Assert.True(registry.TryGet("htdi", out var htdi));

        Assert.False(htdi.IsEnabled("Write:Enabled"));
        Assert.False(htdi.IsEnabled("Bootstrap:UserFields:Enabled"));
        Assert.False(htdi.IsEnabled("SalesDocuments:AllowPostedInvoices"));
        Assert.Null(htdi.Get("TimeAndMaterialsItemCode"));
        Assert.Null(htdi.Get("FiscalProjectCodePattern"));
        // Credenziali e tempi del Service Layer invece si ereditano.
        Assert.Equal("manager", htdi.Get("UserName"));
        Assert.Equal("20", htdi.Get("SessionTimeoutMinutes"));
        // E la principale resta com'era.
        Assert.True(registry.Primary.IsEnabled("Write:Enabled"));
        Assert.Equal("TM", registry.Primary.Get("TimeAndMaterialsItemCode"));
    }

    [Fact]
    public void Le_company_aggiuntive_nascono_in_sola_lettura()
    {
        var chiusa = TestCompanies.Registry(TestCompanies.WithHtdi());
        Assert.True(chiusa.TryGet("htdi", out var htdi));
        Assert.True(htdi.ReadOnly);

        var aperta = TestCompanies.Registry(TestCompanies.WithHtdi(new Dictionary<string, string?>
        {
            ["Companies:htdi:AllowWrites"] = "true",
        }));
        Assert.True(aperta.TryGet("htdi", out var htdiAperta));
        Assert.False(htdiAperta.ReadOnly);
        // Aprire le scritture non accende da solo la scrittura contabile: resta il suo interruttore.
        Assert.False(htdiAperta.IsEnabled("Write:Enabled"));
    }

    public static IEnumerable<object[]> ConfigurazioniSbagliate() => new[]
    {
        new object[] { "chiave su due company", TestCompanies.WithHtdi(new Dictionary<string, string?> { ["Companies:htdi:ApiKeys:0"] = "chiave-mtf" }) },
        new object[] { "aggiuntiva senza schema", TestCompanies.WithHtdi(new Dictionary<string, string?> { ["Companies:htdi:CompanyDB"] = null }) },
        new object[] { "schema servito due volte", TestCompanies.WithHtdi(new Dictionary<string, string?> { ["Companies:htdi:CompanyDB"] = "sbo_mtf" }) },
        new object[] { "id della principale riusato", new Dictionary<string, string?> { ["Companies:default:CompanyDB"] = "SBOHITECH_PROD" } },
        new object[] { "principale senza schema", new Dictionary<string, string?> { ["SapB1:CompanyDB"] = null } },
    };

    [Theory]
    [MemberData(nameof(ConfigurazioniSbagliate))]
    public void Una_configurazione_sbagliata_ferma_l_avvio(string caso, Dictionary<string, string?> values)
    {
        Assert.False(string.IsNullOrEmpty(caso));
        Assert.Throws<InvalidOperationException>(() => TestCompanies.Registry(values));
    }

    // ── Contesto della richiesta ──

    [Fact]
    public void Il_contesto_segue_il_claim_del_login_e_senza_claim_rifiuta()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());

        Assert.Equal("SBOHITECH_PROD", ContextFor(registry, User("htdi")).Current.CompanyDB);
        Assert.Equal("SBO_MTF", ContextFor(registry, User("default")).Current.CompanyDB);
        // Nessun ripiego sulla principale: senza company, o con una sconosciuta, si fallisce.
        Assert.Throws<CompanyNotResolvedException>(() => ContextFor(registry, User(null)).Current);
        Assert.Throws<CompanyNotResolvedException>(() => ContextFor(registry, User("sconosciuta")).Current);
    }

    [Fact]
    public void In_background_la_company_si_fissa_esplicitamente()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());
        Assert.True(registry.TryGet("htdi", out var htdi));
        var context = new CompanyContext(registry, new HttpContextAccessor());

        Assert.Throws<CompanyNotResolvedException>(() => context.Current);
        using (context.Use(htdi))
        {
            Assert.Same(htdi, context.Current);
        }
        Assert.Throws<CompanyNotResolvedException>(() => context.Current);
    }

    // ── Cancello davanti agli endpoint ──

    [Theory]
    [InlineData(null, "POST", "/api/auth/login", true)]          // anonimo: login
    [InlineData("", "GET", "/api/gl/lines", false)]              // autenticato senza company
    [InlineData("sconosciuta", "GET", "/api/gl/lines", false)]
    [InlineData("htdi", "GET", "/api/gl/lines", true)]
    [InlineData("htdi", "POST", "/api/gl/lines/by-entry-ids", true)]
    [InlineData("htdi", "POST", "/api/timesheet", false)]
    [InlineData("htdi", "PATCH", "/api/timesheet/7128/hours", false)]
    [InlineData("htdi", "POST", "/api/gl/attribution", false)]
    [InlineData("htdi", "POST", "/api/sales-documents", false)]
    [InlineData("default", "POST", "/api/timesheet", true)]
    public void Il_cancello_vuole_una_company_e_rispetta_la_sola_lettura(string? company, string method, string path, bool allowed)
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (company != null) context.User = User(company == string.Empty ? null : company);

        Assert.Equal(allowed, CompanyGate.Check(context, registry).Allowed);
    }

    // ── Chiave API → company ──

    [Fact]
    public async Task La_chiave_api_mette_la_company_nell_identita()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());

        var htdi = await AuthenticateAsync(registry, "chiave-htdi");
        Assert.True(htdi.Succeeded);
        Assert.Equal("htdi", htdi.Principal!.FindFirst(CompanyClaims.Company)!.Value);

        var mtf = await AuthenticateAsync(registry, "chiave-mtf");
        Assert.Equal("default", mtf.Principal!.FindFirst(CompanyClaims.Company)!.Value);

        Assert.False((await AuthenticateAsync(registry, "chiave-sbagliata")).Succeeded);
        Assert.True((await AuthenticateAsync(registry, null)).None);
    }

    // ── HANA e Service Layer ──

    [Fact]
    public void Lo_schema_hana_e_quello_della_company()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi());
        Assert.True(registry.TryGet("htdi", out var htdi));

        Assert.Equal("SBOHITECH_PROD", Db(registry, htdi).CurrentSchema);
        Assert.Equal("SBO_MTF", Db(registry, registry.Primary).CurrentSchema);
    }

    [Fact]
    public async Task Le_sessioni_del_service_layer_non_si_condividono_fra_company()
    {
        var registry = TestCompanies.Registry(TestCompanies.WithHtdi(new Dictionary<string, string?>
        {
            ["SapB1:UserName"] = "manager",
            ["SapB1:Password"] = "segreto",
        }));
        Assert.True(registry.TryGet("htdi", out var htdi));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sl = new LoginRecorder();

        await ServiceLayer(registry, htdi, sl, cache).GetSessionIdAsync();
        await ServiceLayer(registry, registry.Primary, sl, cache).GetSessionIdAsync();
        await ServiceLayer(registry, htdi, sl, cache).GetSessionIdAsync();

        // Due login, uno per company: la terza chiamata riusa la sessione di HTDI, non quella di MTF.
        Assert.Equal(2, sl.LoginBodies.Count);
        Assert.Contains("\"CompanyDB\":\"SBOHITECH_PROD\"", sl.LoginBodies[0]);
        Assert.Contains("\"UserName\":\"manager\"", sl.LoginBodies[0]);
        Assert.Contains("\"CompanyDB\":\"SBO_MTF\"", sl.LoginBodies[1]);
        Assert.True(cache.TryGetValue(SapB1ServiceLayerService.CacheKeysFor("htdi", "service-account").sessionKey, out string? htdiSession));
        Assert.True(cache.TryGetValue(SapB1ServiceLayerService.CacheKeysFor("default", "service-account").sessionKey, out string? mtfSession));
        Assert.NotEqual(htdiSession, mtfSession);
    }

    // ── Supporto ──

    private static ClaimsPrincipal User(string? company)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "ax360-erp") };
        if (company != null) claims.Add(new Claim(CompanyClaims.Company, company));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static CompanyContext ContextFor(ICompanyRegistry registry, ClaimsPrincipal user)
        => new(registry, new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } });

    private static DbOdbcService Db(CompanyRegistry registry, CompanyProfile company)
        => new(TestCompanies.Config(), NullLogger<DbOdbcService>.Instance, new HttpContextAccessor(), TestCompanies.Fixed(company));

    private static SapB1ServiceLayerService ServiceLayer(CompanyRegistry registry, CompanyProfile company, HttpMessageHandler handler, IMemoryCache cache)
        => new(new HttpClient(handler), TestCompanies.Config(), NullLogger<SapB1ServiceLayerService>.Instance,
            InterfaceFake<IDbOdbcService>.Create(new()), cache, new HttpContextAccessor(),
            InterfaceFake<ICredentialStore>.Create(new() { ["GetCredentials"] = _ => null }), TestCompanies.Fixed(company));

    private static async Task<AuthenticateResult> AuthenticateAsync(ICompanyRegistry registry, string? key)
    {
        var handler = new ApiKeyAuthenticationHandler(new SchemeOptions(), NullLoggerFactory.Instance, UrlEncoder.Default, registry);
        var context = new DefaultHttpContext();
        if (key != null) context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = key;
        await handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private sealed class SchemeOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    /// <summary>Service Layer finto: registra i login e risponde con una sessione nuova per ognuno.</summary>
    private sealed class LoginRecorder : HttpMessageHandler
    {
        public List<string> LoginBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/Login", StringComparison.Ordinal))
            {
                LoginBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"SessionId\":\"sessione-{LoginBodies.Count}\"}}"),
                };
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/UserFields", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            throw new InvalidOperationException($"Chiamata al Service Layer non prevista: {request.Method} {path}");
        }
    }
}

using AX.SAPB1.Api.Companies;
using Microsoft.Extensions.Configuration;

namespace AX.SAPB1.Api.Tests;

/// <summary>Registro e contesto di company per i test: la principale è SBO_MTF, come in produzione.</summary>
internal static class TestCompanies
{
    public static IConfiguration Config(IDictionary<string, string?>? values = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["SapB1:CompanyDB"] = "SBO_MTF",
            ["SapB1:ServiceLayerUrl"] = "https://sl.test/b1s/v1/",
            ["ConnectionStrings:DefaultDatabase"] = "DSN=test",
        };
        if (values != null)
        {
            foreach (var kv in values) all[kv.Key] = kv.Value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(all).Build();
    }

    public static CompanyRegistry Registry(IDictionary<string, string?>? values = null) => new(Config(values));

    /// <summary>La configurazione con HTDI come company aggiuntiva, con chiavi API distinte.</summary>
    public static Dictionary<string, string?> WithHtdi(IDictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:ApiKeys:0"] = "chiave-mtf",
            ["Companies:htdi:CompanyDB"] = "SBOHITECH_PROD",
            ["Companies:htdi:ApiKeys:0"] = "chiave-htdi",
        };
        if (extra != null)
        {
            foreach (var kv in extra) values[kv.Key] = kv.Value;
        }
        return values;
    }

    public static ICompanyContext Fixed(CompanyProfile company) => new FixedContext(company);

    private sealed class FixedContext : ICompanyContext
    {
        private CompanyProfile _company;
        public FixedContext(CompanyProfile company) => _company = company;
        public CompanyProfile Current => _company;

        public IDisposable Use(CompanyProfile company)
        {
            var previous = _company;
            _company = company;
            return new Restore(() => _company = previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Action _action;
            public Restore(Action action) => _action = action;
            public void Dispose() => _action();
        }
    }
}

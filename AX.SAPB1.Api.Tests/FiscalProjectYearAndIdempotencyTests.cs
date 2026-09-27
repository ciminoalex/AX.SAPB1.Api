using AX.SAPB1.Api.Services.FiscalProjects;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Anno della numerazione dei progetti contabili e idempotenza della creazione. L'anno del codice è quello
/// della CREAZIONE (o quello esplicito del chiamante), mai quello di validFrom, che il portale retrodata: un
/// progetto del 2024 creato oggi, o una creazione di gennaio, finirebbe nella serie di un anno chiuso, e un
/// codice OPRJ usato in contabilità non si rinomina. Una risposta persa non deve creare un secondo progetto.
/// </summary>
public class FiscalProjectYearAndIdempotencyTests
{
    [Theory]
    [InlineData(2026, "2027-01-10T10:00:00Z", 2026)] // anno esplicito del chiamante
    [InlineData(null, "2026-09-27T10:00:00Z", 2026)]
    [InlineData(null, "2026-12-31T23:30:00Z", 2027)] // a Roma è già Capodanno
    [InlineData(12, "2026-09-27T10:00:00Z", 2026)]   // anno fuori scala: si ignora
    public void Anno_del_codice(int? requested, string nowUtc, int expected)
    {
        var now = DateTime.Parse(nowUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.Equal(expected, FiscalProjectService.ResolveCodeYear(requested, now));
    }

    [Fact]
    public void La_stessa_chiave_ritrova_il_progetto_creato()
    {
        var memory = new FiscalProjectIdempotency();
        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(memory.TryGet("progetto-1", now, out _));
        memory.Remember("progetto-1", "PRJ26_2600131", now);

        Assert.True(memory.TryGet(" progetto-1 ", now.AddMinutes(5), out var code));
        Assert.Equal("PRJ26_2600131", code);
        Assert.False(memory.TryGet("progetto-2", now, out _));
        Assert.False(memory.TryGet(null, now, out _));
    }

    [Fact]
    public void Dopo_il_periodo_di_conservazione_la_chiave_non_vale_piu()
    {
        var memory = new FiscalProjectIdempotency();
        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
        memory.Remember("progetto-1", "PRJ26_2600131", now);

        Assert.False(memory.TryGet("progetto-1", now + FiscalProjectIdempotency.Retention + TimeSpan.FromMinutes(1), out _));
    }
}

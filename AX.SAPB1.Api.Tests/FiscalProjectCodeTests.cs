using AX.SAPB1.Api.Services.FiscalProjects;
using AX.SAPB1.Api.Support;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Il prossimo codice dei progetti contabili (OPRJ). La numerazione in uso è <c>PRJ26_2600016</c>: anno a due
/// cifre, trattino basso, di nuovo l'anno e un progressivo annuale di 5 cifre. Due trappole: in LIKE il '_' è
/// un jolly (senza ESCAPE si leggono codici di altri formati), e un codice creato a mano con un formato
/// diverso non deve spostare la numerazione.
/// </summary>
public class FiscalProjectCodeTests
{
    private static readonly FiscalProjectCodePattern Default = FiscalProjectCodePattern.Parse(null);

    [Fact]
    public void Il_primo_codice_dell_anno_e_00001()
        => Assert.Equal("PRJ26_2600001", Default.Next(2026, Array.Empty<string>()));

    [Fact]
    public void Il_prossimo_codice_e_il_massimo_piu_uno_anche_con_buchi()
        => Assert.Equal("PRJ26_2600131", Default.Next(2026, new[] { "PRJ26_2600016", "PRJ26_2600130", "PRJ26_2600002" }));

    [Theory]
    [InlineData("PRJ26_260099")]       // una cifra in meno
    [InlineData("PRJ26_26000999")]     // una cifra in più
    [InlineData("PRJ26_26ABCDE")]      // non numerico
    [InlineData("PRJ25_2500500")]      // anno diverso
    [InlineData("PRJ26X2699999")]      // '_' letto come jolly da un LIKE senza escape
    [InlineData(null)]
    public void I_codici_fuori_formato_non_spostano_la_numerazione(string? intruso)
        => Assert.Equal("PRJ26_2600017", Default.Next(2026, new[] { "PRJ26_2600016", intruso }));

    [Fact]
    public void Gli_spazi_di_coda_di_SAP_non_escludono_un_codice()
        => Assert.Equal("PRJ26_2600017", Default.Next(2026, new[] { "PRJ26_2600016   " }));

    [Fact]
    public void Il_pattern_LIKE_protegge_il_trattino_basso()
        => Assert.Equal(@"PRJ26\_26%", Default.LikePattern(2026));

    [Fact]
    public void Un_pattern_personalizzato_con_anno_a_quattro_cifre_e_suffisso()
    {
        var p = FiscalProjectCodePattern.Parse("C{yyyy}-{seq4}/X");
        Assert.Equal("C2027-", p.Prefix(2027));
        Assert.Equal("/X", p.Suffix(2027));
        Assert.Equal("C2027-0008/X", p.Next(2027, new[] { "C2027-0007/X", "C2026-0100/X" }));
        Assert.Equal("C2027-%/X", p.LikePattern(2027));
    }

    [Theory]
    [InlineData("PRJ{yy}")]                 // manca il progressivo
    [InlineData("PRJ{yy}{seq5}{seq2}")]     // due progressivi
    [InlineData("PRJ{mm}{seq5}")]           // segnaposto sconosciuto
    [InlineData("PRJ{seq0}")]               // cifre non valide
    public void Un_pattern_non_valido_e_rifiutato(string pattern)
        => Assert.Throws<ArgumentException>(() => FiscalProjectCodePattern.Parse(pattern));

    [Fact]
    public void Il_progressivo_esaurito_e_un_errore()
    {
        var p = FiscalProjectCodePattern.Parse("P{yy}{seq1}");
        Assert.Throws<InvalidOperationException>(() => p.Next(2026, new[] { "P269" }));
    }

    [Fact]
    public void Un_codice_oltre_la_colonna_PrjCode_e_un_errore()
    {
        var p = FiscalProjectCodePattern.Parse("PROGETTO-LUNGO-{yyyy}-{seq5}");
        Assert.Throws<InvalidOperationException>(() => p.Next(2026, Array.Empty<string>()));
    }

    [Fact]
    public void Il_payload_OPRJ_porta_codice_nome_attivo_e_date()
    {
        var payload = FiscalProjectService.BuildPayload("PRJ26_2600131", "Comal – Consulenza", new DateTime(2026, 10, 1), null);
        Assert.Equal("PRJ26_2600131", payload["Code"]);
        Assert.Equal("Comal – Consulenza", payload["Name"]);
        Assert.Equal("tYES", payload["Active"]);
        Assert.Equal("2026-10-01", payload["ValidFrom"]);
        Assert.False(payload.ContainsKey("ValidTo"));

        var conFine = FiscalProjectService.BuildPayload("PRJ26_2600131", "X", new DateTime(2026, 10, 1), new DateTime(2027, 9, 30));
        Assert.Equal("2027-09-30", conFine["ValidTo"]);
    }
}

/// <summary>
/// Il lock per correlationId dei documenti di vendita: stessa chiave in fila, chiavi diverse in parallelo,
/// e nessuna voce che resta nel dizionario dopo il rilascio.
/// </summary>
public class KeyedAsyncLockTests
{
    [Fact]
    public async Task La_stessa_chiave_si_serializza()
    {
        var locks = new KeyedAsyncLock();
        var first = await locks.AcquireAsync("fattura-1");
        var second = locks.AcquireAsync("FATTURA-1"); // stessa chiave, maiuscole diverse

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, locks.ActiveKeys);
    }

    [Fact]
    public async Task Chiavi_diverse_procedono_insieme()
    {
        var locks = new KeyedAsyncLock();
        using var a = await locks.AcquireAsync("fattura-1");
        using var b = await locks.AcquireAsync("fattura-2").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, locks.ActiveKeys);
    }

    [Fact]
    public async Task Al_rilascio_la_voce_sparisce()
    {
        var locks = new KeyedAsyncLock();
        var handle = await locks.AcquireAsync("fattura-1");
        handle.Dispose();
        handle.Dispose(); // doppio Dispose innocuo
        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task Un_attesa_annullata_non_lascia_voci_orfane()
    {
        var locks = new KeyedAsyncLock();
        var held = await locks.AcquireAsync("fattura-1");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.AcquireAsync("fattura-1", cts.Token));
        held.Dispose();
        Assert.Equal(0, locks.ActiveKeys);
    }
}

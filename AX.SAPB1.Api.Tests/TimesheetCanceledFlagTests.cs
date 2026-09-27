using System.Text.Json;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.Timesheets;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Il campo <c>canceled</c> della ricerca per risorsa e date (<c>GET /api/timesheet/employee/{resId}/daterange</c>),
/// che il portale usa per ritrovare una riga dopo un push andato in timeout. La query restituisce anche le righe
/// annullate e non le filtra (serve ad altri chiamanti): il campo dice quali sono. Un valore illeggibile resta
/// <c>null</c>, mai <c>false</c>: "attiva" detto senza saperlo farebbe scambiare una riga annullata per quella del push.
/// </summary>
public class TimesheetCanceledFlagTests
{
    [Theory]
    [MemberData(nameof(Valori))]
    public void Il_valore_grezzo_di_Canceled_diventa_true_false_o_null(object? raw, bool? atteso)
        => Assert.Equal(atteso, DbOdbcService.ParseCanceledFlag(raw));

    public static IEnumerable<object?[]> Valori()
    {
        yield return new object?[] { "Y", true };
        yield return new object?[] { "y", true };
        yield return new object?[] { " Y ", true };
        yield return new object?[] { 'Y', true };
        yield return new object?[] { "N", false };
        yield return new object?[] { "n", false };
        yield return new object?[] { "N   ", false };
        yield return new object?[] { 'N', false };
        // Assente o illeggibile: "non lo so", non "attiva".
        yield return new object?[] { null, null };
        yield return new object?[] { DBNull.Value, null };
        yield return new object?[] { "", null };
        yield return new object?[] { "   ", null };
        yield return new object?[] { "X", null };
        yield return new object?[] { "YES", null };
        yield return new object?[] { "tYES", null };
        yield return new object?[] { 1, null };
        yield return new object?[] { 0, null };
    }

    [Fact]
    public void Nel_JSON_il_campo_si_chiama_canceled_e_resta_null_se_non_letto()
    {
        // Stesse impostazioni di serializzazione delle risposte MVC (camelCase, null scritti).
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var annullata = JsonDocument.Parse(JsonSerializer.Serialize(new Timesheet { DocEntry = 7128, Canceled = true }, web));
        using var nonLetta = JsonDocument.Parse(JsonSerializer.Serialize(new Timesheet { DocEntry = 7128 }, web));

        Assert.Equal(JsonValueKind.True, annullata.RootElement.GetProperty("canceled").ValueKind);
        Assert.Equal(JsonValueKind.Null, nonLetta.RootElement.GetProperty("canceled").ValueKind);
    }
}

/// <summary>
/// Il 404 della riga assente in <c>PATCH /api/timesheet/{docEntry}/hours</c> porta SEMPRE il corpo con
/// <c>outcome = "not_found"</c>: è l'unico segnale con cui il portale distingue la riga assente (che blocca la riga)
/// dalla rotta assente di un servizio non aggiornato (404 senza corpo o senza <c>outcome</c>, che fa solo ritentare).
/// </summary>
public class TimesheetHoursNotFoundContractTests
{
    [Fact]
    public void Riga_assente_risponde_404_con_outcome_not_found_e_ore_null()
    {
        var decision = TimesheetHoursRules.Decide(null, new TimesheetHoursUpdateRequest { Hours = 6.5m, BillableHours = 4m });
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(decision.ToResult(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(404, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.Equal("not_found", body.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("currentHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("currentBillableHours").ValueKind);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("message").ValueKind);
    }
}

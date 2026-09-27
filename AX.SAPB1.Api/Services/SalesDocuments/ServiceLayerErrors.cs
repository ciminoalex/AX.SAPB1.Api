using Newtonsoft.Json.Linq;

namespace AX.SAPB1.Api.Services.SalesDocuments
{
    /// <summary>
    /// Lettura degli errori del Service Layer, che risponde così:
    /// <c>{ "error": { "code": -2035, "message": { "lang": "en-us", "value": "This entry already exists ..." } } }</c>
    /// (su alcune versioni <c>message</c> è direttamente una stringa).
    /// </summary>
    public static class ServiceLayerErrors
    {
        /// <summary>Codice SAP di "chiave già esistente" (ODBC -2035).</summary>
        internal const int AlreadyExistsCode = -2035;

        /// <summary>Il messaggio leggibile di SAP; se il corpo non è l'errore atteso, il corpo stesso (accorciato).</summary>
        public static string ExtractMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(nessun dettaglio)";
            var error = SalesDocumentPayloadBuilder.ParseBody(body)?["error"];
            var message = error?["message"];
            var text = message switch
            {
                JObject o => o["value"]?.ToString(),
                JValue v => v.ToString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(text)) return text!.Trim();
            var trimmed = body.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
        }

        /// <summary>
        /// Vero se SAP ha rifiutato perché la chiave esiste già: è il segnale per ricalcolare il codice e
        /// ritentare (qualcuno ha creato lo stesso codice dal client SAP o da un'altra istanza).
        /// </summary>
        public static bool IsAlreadyExists(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            var code = SalesDocumentPayloadBuilder.ReadInt(SalesDocumentPayloadBuilder.ParseBody(body)?["error"]?["code"]);
            if (code == AlreadyExistsCode) return true;
            var message = ExtractMessage(body);
            return message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                || message.Contains("-2035", StringComparison.Ordinal)
                || message.Contains("esiste già", StringComparison.OrdinalIgnoreCase);
        }
    }
}

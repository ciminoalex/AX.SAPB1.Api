using System.Collections.Concurrent;

namespace AX.SAPB1.Api.Services.FiscalProjects
{
    /// <summary>
    /// Memoria delle creazioni di progetti contabili per chiave di idempotenza del chiamante (l'Id del progetto
    /// del portale). Senza codice esplicito ogni POST crea un progetto nuovo ("massimo + 1"): se la risposta si
    /// perde dopo che SAP ha creato l'OPRJ (timeout del portale, errore di rete) e l'utente riprova, nascerebbe
    /// un secondo progetto e il primo resterebbe orfano e attivo. Qui la ripetizione ritrova il primo.
    ///
    /// <para><b>Limite dichiarato.</b> La memoria è del processo: un riavvio del servizio fra la risposta persa e
    /// il nuovo tentativo non la conserva. È il caso raro (la finestra è di pochi minuti); OPRJ non ha un campo in
    /// cui scrivere la chiave senza aggiungere un campo utente alla company, e i metadati di produzione non si
    /// toccano.</para>
    /// </summary>
    public sealed class FiscalProjectIdempotency
    {
        /// <summary>Per quanto tempo una chiave ritrova il progetto creato.</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

        /// <summary>Istanza del processo (il servizio è Scoped: la memoria deve sopravvivere alla richiesta).</summary>
        public static FiscalProjectIdempotency Shared { get; } = new();

        private readonly ConcurrentDictionary<string, (string Code, DateTime AtUtc)> _byKey = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Codice già creato per la chiave, se ancora nel periodo di conservazione.</summary>
        public bool TryGet(string? key, DateTime utcNow, out string code)
        {
            code = string.Empty;
            if (string.IsNullOrWhiteSpace(key) || !_byKey.TryGetValue(key.Trim(), out var entry)) return false;
            if (utcNow - entry.AtUtc > Retention)
            {
                _byKey.TryRemove(key.Trim(), out _);
                return false;
            }
            code = entry.Code;
            return true;
        }

        /// <summary>Ricorda il codice creato per la chiave (e ripulisce le voci scadute).</summary>
        public void Remember(string? key, string code, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(code)) return;
            _byKey[key.Trim()] = (code, utcNow);
            foreach (var stale in _byKey.Where(kv => utcNow - kv.Value.AtUtc > Retention).Select(kv => kv.Key).ToList())
                _byKey.TryRemove(stale, out _);
        }
    }
}

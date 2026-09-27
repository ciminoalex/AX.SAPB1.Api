using System.Collections.Concurrent;

namespace AX.SAPB1.Api.Services.SalesDocuments
{
    /// <summary>
    /// Creazioni di documenti di vendita finite con esito incerto: la chiamata al Service Layer è andata in timeout
    /// o in errore di trasporto, quindi SAP può aver creato il documento DOPO che il servizio ha smesso di
    /// aspettare (il client HTTP verso il Service Layer rinuncia a 100 secondi, il Service Layer no).
    ///
    /// <para><b>Perché serve.</b> Il lock per correlazione tiene la lettura dello stato in attesa finché la
    /// creazione è in corso nel processo; ma quando la creazione si è arresa per timeout il lock è già libero e
    /// la scrittura in SAP può ancora arrivare. Se in quella finestra il portale chiede lo stato per annullare il
    /// suo documento, «non trovato» gli farebbe liberare le ore per un documento che compare pochi secondi dopo:
    /// doppia fatturazione. Per <see cref="Window"/> dopo un esito incerto «non trovato» non si dice: la lettura
    /// risponde «non verificabile» e il portale non annulla (fail-closed). Se il documento si trova, vale lui.</para>
    ///
    /// <para><b>Limite dichiarato.</b> Memoria del processo, come il lock: un riavvio la dimentica. La finestra
    /// copre il caso reale (una scrittura rallentata di qualche minuto), non un Service Layer fermo per ore.</para>
    /// </summary>
    public sealed class UncertainCreations
    {
        /// <summary>Per quanto tempo, dopo un esito incerto, «non trovato» non vale come risposta.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        /// <summary>Istanza del processo (il servizio è Scoped: la memoria deve sopravvivere alla richiesta).</summary>
        public static UncertainCreations Shared { get; } = new();

        private readonly ConcurrentDictionary<string, DateTime> _sinceUtc = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registra un esito incerto per la correlazione (e ripulisce le voci scadute).</summary>
        public void Mark(string? correlationId, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(correlationId)) return;
            _sinceUtc[correlationId.Trim()] = utcNow;
            foreach (var stale in _sinceUtc.Where(kv => utcNow - kv.Value > Window).Select(kv => kv.Key).ToList())
                _sinceUtc.TryRemove(stale, out _);
        }

        /// <summary>True se la correlazione ha un esito incerto ancora dentro la finestra; <paramref name="sinceUtc"/> è quando.</summary>
        public bool IsUncertain(string? correlationId, DateTime utcNow, out DateTime sinceUtc)
        {
            sinceUtc = default;
            if (string.IsNullOrWhiteSpace(correlationId) || !_sinceUtc.TryGetValue(correlationId.Trim(), out var at)) return false;
            if (utcNow - at > Window)
            {
                _sinceUtc.TryRemove(correlationId.Trim(), out _);
                return false;
            }
            sinceUtc = at;
            return true;
        }
    }
}

namespace AX.SAPB1.Api.Support
{
    /// <summary>
    /// Lock asincrono per chiave, dentro il processo: due richieste con la stessa chiave si serializzano,
    /// chiavi diverse procedono in parallelo. Serve all'idempotenza dei documenti di vendita: "cerca il
    /// correlationId, se non c'è crea" non è atomico (in SAP non esiste un vincolo univoco sull'UDF), quindi
    /// due push concorrenti dello stesso documento creerebbero due documenti. Le voci si rimuovono al rilascio
    /// dell'ultimo utilizzatore, così il dizionario non cresce con un GUID per fattura.
    /// <para>Protegge un solo processo: due istanze del servizio sulla STESSA company non sono coperte (e
    /// infatti ogni company ha la sua istanza).</para>
    /// </summary>
    public sealed class KeyedAsyncLock
    {
        private readonly Dictionary<string, Entry> _entries;
        private readonly object _gate = new();

        public KeyedAsyncLock(IEqualityComparer<string>? comparer = null)
        {
            _entries = new Dictionary<string, Entry>(comparer ?? StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Numero di chiavi con almeno un utilizzatore (per i test).</summary>
        internal int ActiveKeys
        {
            get { lock (_gate) return _entries.Count; }
        }

        public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry();
                    _entries[key] = entry;
                }
                entry.RefCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                Release(key, entry, releaseSemaphore: false);
                throw;
            }

            return new Releaser(this, key, entry);
        }

        private void Release(string key, Entry entry, bool releaseSemaphore)
        {
            if (releaseSemaphore) entry.Semaphore.Release();
            lock (_gate)
            {
                entry.RefCount--;
                if (entry.RefCount == 0) _entries.Remove(key);
            }
        }

        private sealed class Entry
        {
            public readonly SemaphoreSlim Semaphore = new(1, 1);
            public int RefCount;
        }

        private sealed class Releaser : IDisposable
        {
            private KeyedAsyncLock? _owner;
            private readonly string _key;
            private readonly Entry _entry;

            public Releaser(KeyedAsyncLock owner, string key, Entry entry)
            {
                _owner = owner;
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Release(_key, _entry, releaseSemaphore: true);
            }
        }
    }
}

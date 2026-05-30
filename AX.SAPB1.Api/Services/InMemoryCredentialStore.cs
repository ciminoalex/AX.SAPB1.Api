using System.Collections.Concurrent;

namespace AX.SAPB1.Api.Services
{
    public class InMemoryCredentialStore : ICredentialStore
    {
        private readonly ConcurrentDictionary<string, (string userName, string password)> _store = new();

        public void SaveCredentials(string userKey, string userName, string password)
        {
            _store[userKey] = (userName, password);
        }

        public (string userName, string password)? GetCredentials(string userKey)
        {
            return _store.TryGetValue(userKey, out var creds) ? creds : null;
        }

        public void RemoveCredentials(string userKey)
        {
            _store.TryRemove(userKey, out _);
        }
    }
}



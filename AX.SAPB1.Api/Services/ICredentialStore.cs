namespace AX.SAPB1.Api.Services
{
    public interface ICredentialStore
    {
        void SaveCredentials(string userKey, string userName, string password);
        (string userName, string password)? GetCredentials(string userKey);
        void RemoveCredentials(string userKey);
    }
}



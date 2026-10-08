namespace AX.SAPB1.Api.Services
{
    public interface ISapB1AuthService
    {
        /// <summary>Verifica utente e password facendo il login al Service Layer sulla company <paramref name="companyDb"/>.</summary>
        Task<bool> ValidateCredentialsAsync(string userName, string password, string companyDb, CancellationToken cancellationToken = default);
    }
}



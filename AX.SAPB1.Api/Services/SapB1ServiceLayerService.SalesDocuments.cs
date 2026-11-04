using System.Net.Http.Headers;
using System.Text;
using AX.SAPB1.Api.Services.SalesDocuments;
using Newtonsoft.Json;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Chiamate di scrittura del Service Layer per i documenti di vendita del portale e per i progetti
    /// contabili. Sono volutamente "sottili": inviano e restituiscono l'esito grezzo
    /// (<see cref="ServiceLayerResponse"/>). Cosa inviare lo decide <see cref="SalesDocumentPayloadBuilder"/>
    /// (puro, testato), cosa fare dell'esito lo decidono <c>SalesDocumentService</c> e
    /// <c>FiscalProjectService</c>. Nessuna di queste chiamate lancia su un rifiuto di SAP.
    /// </summary>
    public partial class SapB1ServiceLayerService
    {
        private static readonly JsonSerializerSettings IgnoreNulls = new() { NullValueHandling = NullValueHandling.Ignore };

        /// <summary>POST del documento sull'entità indicata (<c>Invoices</c>, <c>Orders</c> o <c>Drafts</c>).</summary>
        public Task<ServiceLayerResponse> CreateSalesDocumentAsync(string entity, IReadOnlyDictionary<string, object?> payload)
            => PostJsonAsync(entity, payload);

        /// <summary>Rilettura dei totali di un documento appena creato, se la risposta di creazione non li riportava.</summary>
        public async Task<ServiceLayerResponse> GetSalesDocumentAsync(string entity, int docEntry)
        {
            await GetSessionIdAsync();
            var response = await ExecuteWithRetryAsync(() =>
                _httpClient.GetAsync($"{entity}({docEntry})?$select=DocEntry,DocNum,DocTotal,VatSum,DocDueDate"));
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }

        /// <summary>
        /// Carica i file in <c>Attachments2</c> (una sola voce con più righe) e restituisce la risposta, che
        /// contiene <c>AbsoluteEntry</c> da mettere in <c>AttachmentEntry</c> sul documento.
        /// <para>Il contenuto multipart si costruisce DENTRO la lambda: <see cref="ExecuteWithRetryAsync"/> la
        /// riesegue su un 401, e un contenuto già inviato non si rispedisce. Il boundary va SENZA virgolette
        /// nell'header: .NET le mette di default, e il Service Layer non riconosce le parti. Anche il
        /// Content-Disposition è scritto a mano, senza il <c>filename*</c> che .NET aggiungerebbe.</para>
        /// </summary>
        public async Task<ServiceLayerResponse> UploadAttachmentsAsync(IReadOnlyList<ServiceLayerFile> files)
        {
            await GetSessionIdAsync();
            var response = await ExecuteWithRetryAsync(() =>
            {
                var boundary = "AX360-" + Guid.NewGuid().ToString("N");
                var content = new MultipartFormDataContent(boundary);
                content.Headers.Remove("Content-Type");
                content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);
                foreach (var file in files)
                {
                    var part = new ByteArrayContent(file.Content);
                    part.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"files\"; filename=\"{file.FileName}\"");
                    part.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType)
                        ? mediaType
                        : new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(part);
                }
                return _httpClient.PostAsync("Attachments2", content);
            });
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }

        /// <summary>GET <c>Attachments2(absoluteEntry)</c>: righe della voce allegati.</summary>
        public async Task<ServiceLayerResponse> GetAttachmentsAsync(int absoluteEntry)
        {
            await GetSessionIdAsync();
            var response = await ExecuteWithRetryAsync(() => _httpClient.GetAsync($"Attachments2({absoluteEntry})"));
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }

        /// <summary>
        /// PATCH <c>Attachments2(absoluteEntry)</c> con <c>B1S-ReplaceCollectionsOnPatch: true</c>: le righe inviate
        /// sostituiscono quelle esistenti. Le righe di Attachments2 non hanno chiave (niente <c>LineNum</c> nel
        /// <c>$metadata</c>): senza l'header una riga inviata viene AGGIUNTA, e SAP risponde «file già esistente».
        /// Provato in produzione il 4/11/2026 sulle 21 bozze d'ordine di ottobre.
        /// </summary>
        public async Task<ServiceLayerResponse> ReplaceAttachmentLinesAsync(int absoluteEntry, IReadOnlyDictionary<string, object?> payload)
        {
            await GetSessionIdAsync();
            var json = JsonConvert.SerializeObject(payload, IgnoreNulls);
            // Richiesta e contenuto dentro la lambda: ExecuteWithRetryAsync la riesegue sul 401.
            var response = await ExecuteWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Patch, $"Attachments2({absoluteEntry})")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("B1S-ReplaceCollectionsOnPatch", "true");
                return _httpClient.SendAsync(request);
            });
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }

        /// <summary>DELETE <c>Drafts(docEntry)</c>: elimina una bozza (che sia una bozza viva lo verifica il chiamante).</summary>
        public async Task<ServiceLayerResponse> DeleteDraftAsync(int docEntry)
        {
            await GetSessionIdAsync();
            var response = await ExecuteWithRetryAsync(() => _httpClient.DeleteAsync($"Drafts({docEntry})"));
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }

        /// <summary>POST <c>Projects</c>: crea un progetto contabile (OPRJ).</summary>
        public Task<ServiceLayerResponse> CreateFinancialProjectAsync(IReadOnlyDictionary<string, object?> payload)
            => PostJsonAsync("Projects", payload);

        private async Task<ServiceLayerResponse> PostJsonAsync(string entity, IReadOnlyDictionary<string, object?> payload)
        {
            await GetSessionIdAsync();
            var json = JsonConvert.SerializeObject(payload, IgnoreNulls);
            // StringContent dentro la lambda, per la stessa ragione del multipart: la lambda si riesegue sul 401.
            var response = await ExecuteWithRetryAsync(() =>
                _httpClient.PostAsync(entity, new StringContent(json, Encoding.UTF8, "application/json")));
            var body = await response.Content.ReadAsStringAsync();
            return new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }
    }
}

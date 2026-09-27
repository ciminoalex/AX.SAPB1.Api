using System.Globalization;
using System.Text;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.SalesDocuments;
using AX.SAPB1.Api.Services.Timesheets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Scrittura delle sole ore di una riga di timesheet già esistente, per <c>PATCH /api/timesheet/{docEntry}/hours</c>.
    /// Chiamata "sottile" come quelle dei documenti di vendita: restituisce l'esito grezzo e non lancia su un rifiuto
    /// di SAP. La prima decisione la prende il controller sul dato ODBC; qui la si RIPRENDE sulla riga appena riletta
    /// dal Service Layer, subito prima del PATCH.
    /// </summary>
    public partial class SapB1ServiceLayerService
    {
        /// <summary>
        /// Aggiorna SOLO <c>U_TimeNrTot</c>, <c>U_TimeNrNet</c> e <c>U_TimeNrNF</c> (ripartiti da
        /// <see cref="TimesheetHoursRules.SplitHours"/>) della riga con il <paramref name="docEntry"/> indicato.
        /// Nessun altro campo nel body del PATCH: stato, date, descrizioni e documento di destinazione restano quelli
        /// che SAP ha.
        /// <para>
        /// <b>Perché si ridecide qui.</b> Fra la lettura ODBC del controller e il PATCH ci sono la validazione della
        /// sessione (o un login, che si è già visto restare appeso fino al timeout) e la ricerca del <c>Code</c>: una
        /// finestra di durata non limitata in cui SGS può fatturare la riga o un utente modificarla. Allora la riga
        /// restituita dalla ricerca — che porta <c>Canceled</c>, <c>U_Status</c>, <c>U_DestEntry</c>,
        /// <c>U_TimeNrTot</c> e <c>U_TimeNrNet</c> — non si usa solo per il <c>Code</c>: su di essa si riesegue
        /// <see cref="TimesheetHoursRules.Decide"/> con la stessa richiesta, e se l'esito non è più «updated» il PATCH
        /// non parte (<see cref="TimesheetHoursWriteResult.Superseded"/>). La finestra scoperta si riduce così al solo
        /// giro fra la ricerca e il PATCH.
        /// </para>
        /// <para>
        /// <b>Chiamante andato via.</b> <paramref name="cancellationToken"/> è quello della richiesta HTTP: si controlla
        /// dopo la sessione, sulla ricerca e subito prima del PATCH. Se il portale ha già abbandonato (timeout), non si
        /// scrive: altrimenti una scrittura "fantasma" arrivata tardi farebbe sembrare al tentativo successivo la riga
        /// modificata a mano in SAP (falso <c>changed_in_erp</c>). Il PATCH, una volta partito, si aspetta fino in
        /// fondo (senza token) per sapere com'è andato e registrarlo nel log.
        /// </para>
        /// <para>
        /// <b>Niente nuovo tentativo automatico del PATCH dopo un 401.</b> A differenza delle altre chiamate
        /// (<c>ExecuteWithRetryAsync</c>), un 401 sul PATCH non porta a un nuovo login seguito da un secondo invio:
        /// il login può durare quanto vuole e la decisione presa sulla riga appena riletta invecchierebbe di nuovo.
        /// Il rifiuto torna al portale come errore, e il suo tentativo successivo rilegge tutto da capo.
        /// </para>
        /// <para>
        /// Indirizzamento della riga come in <see cref="UpdateTimesheetAsync"/>: il Service Layer espone
        /// <c>SGS_PRJ_OTMS</c> per <c>Code</c>, quindi prima si cerca la riga con <c>$filter=DocEntry eq …</c> e se
        /// ne legge il <c>Code</c>, poi si fa il PATCH su <c>SGS_PRJ_OTMS('{Code}')</c>. Un errore della ricerca NON
        /// diventa "riga non trovata". <see cref="UpdateTimesheetAsync"/> resta com'era, e continua a forzare
        /// <c>U_TimeNrNet = U_TimeNrTot</c>: non va usato per le righe con ore fatturabili ridotte.
        /// </para>
        /// </summary>
        /// <param name="known">
        /// Stato letto via ODBC dal chiamante. Vale SOLO per le colonne che il Service Layer non espone affatto nella
        /// risposta (chiave assente): una colonna presente, anche a <c>null</c>, vince sempre, perché è più fresca.
        /// </param>
        public async Task<TimesheetHoursWriteResult> UpdateTimesheetHoursAsync(
            int docEntry, TimesheetHoursUpdateRequest request, TimesheetHoursState? known, CancellationToken cancellationToken = default)
        {
            var validationError = TimesheetHoursRules.ValidateUpdate(request);
            if (validationError is not null)
                throw new ArgumentException(validationError, nameof(request));

            await GetSessionIdAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var lookup = await ExecuteWithRetryAsync(() =>
                _httpClient.GetAsync($"SGS_PRJ_OTMS?$filter=DocEntry eq {docEntry}", cancellationToken));
            var lookupBody = await lookup.Content.ReadAsStringAsync(cancellationToken);
            if (!lookup.IsSuccessStatusCode)
                return TimesheetHoursWriteResult.FromResponse(new ServiceLayerResponse((int)lookup.StatusCode, false, lookupBody), null);

            var fresh = ReadServiceLayerHoursState(lookupBody, docEntry, known);
            if (fresh?.Code is null)
                return TimesheetHoursWriteResult.FromResponse(
                    new ServiceLayerResponse(404, false, $"Riga di timesheet con DocEntry {docEntry} non trovata nel Service Layer"), null);

            var recheck = TimesheetHoursRules.Decide(fresh, request);
            if (!recheck.ShouldWrite)
                return TimesheetHoursWriteResult.NotWritten(recheck, fresh);

            var (total, billable, nonBillable) = TimesheetHoursRules.SplitHours(request.Hours!.Value, request.BillableHours);
            var json = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["U_TimeNrTot"] = total,
                ["U_TimeNrNet"] = billable,
                ["U_TimeNrNF"] = nonBillable,
            });
            var key = fresh.Code.Replace("'", "''");

            // Ultimo punto in cui si può ancora rinunciare senza effetti in SAP.
            cancellationToken.ThrowIfCancellationRequested();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync($"SGS_PRJ_OTMS('{key}')", content);
            var body = await response.Content.ReadAsStringAsync();
            return TimesheetHoursWriteResult.FromResponse(
                new ServiceLayerResponse((int)response.StatusCode, response.IsSuccessStatusCode, body), fresh);
        }

        /// <summary>
        /// Stato della riga con il <paramref name="docEntry"/> atteso nella risposta di
        /// <c>SGS_PRJ_OTMS?$filter=DocEntry eq …</c>; null se la riga non c'è o non ha un <c>Code</c>. Si ricontrolla il
        /// <c>DocEntry</c> della riga restituita: scrivere sulla riga sbagliata vorrebbe dire cambiare le ore di un
        /// altro timesheet.
        /// <para>
        /// Stessi parser tolleranti della lettura ODBC (<see cref="DbOdbcService.ParseErpText"/>,
        /// <see cref="DbOdbcService.ParseNullableHours"/>): le ore possono arrivare come numero o come testo, e un
        /// valore illeggibile resta <c>null</c>, mai uno zero inventato. I decimali si leggono come
        /// <see cref="decimal"/>, non come <see cref="double"/>, per non introdurre scarti di arrotondamento nei
        /// confronti a un millesimo. Una colonna assente dalla risposta (non esposta dal Service Layer) prende il valore
        /// di <paramref name="known"/>, se c'è.
        /// </para>
        /// </summary>
        internal static TimesheetHoursState? ReadServiceLayerHoursState(string body, int docEntry, TimesheetHoursState? known = null)
        {
            var row = FindTimesheetRow(body, docEntry);
            if (row is null) return null;

            var code = DbOdbcService.ParseErpText(RawValue(row, "Code"));
            if (code is null) return null;

            string? Text(string name, string? fallback)
                => row.ContainsKey(name) ? DbOdbcService.ParseErpText(RawValue(row, name)) : fallback;
            decimal? Hours(string name, decimal? fallback)
                => row.ContainsKey(name) ? DbOdbcService.ParseNullableHours(RawValue(row, name)) : fallback;

            return new TimesheetHoursState
            {
                DocEntry = docEntry,
                Code = code,
                Canceled = Text("Canceled", known?.Canceled),
                Status = Text("U_Status", known?.Status),
                DestEntry = Text("U_DestEntry", known?.DestEntry),
                TotalHours = Hours("U_TimeNrTot", known?.TotalHours),
                BillableHours = Hours("U_TimeNrNet", known?.BillableHours),
            };
        }

        private static JObject? FindTimesheetRow(string body, int docEntry)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            using var reader = new JsonTextReader(new StringReader(body))
            {
                FloatParseHandling = FloatParseHandling.Decimal,
                DateParseHandling = DateParseHandling.None,
            };
            var items = JObject.Load(reader)["value"] as JArray;
            return items?.OfType<JObject>().FirstOrDefault(r =>
                int.TryParse(r["DocEntry"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var entry)
                && entry == docEntry);
        }

        /// <summary>Valore grezzo di una proprietà JSON (numero, testo o null) per i parser tolleranti.</summary>
        private static object? RawValue(JObject row, string name) => row[name] switch
        {
            null => null,
            JValue value => value.Value,
            var token => token.ToString(Formatting.None),
        };
    }
}

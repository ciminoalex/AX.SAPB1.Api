using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Support;

namespace AX.SAPB1.Api.Services.SalesDocuments
{
    /// <summary>Esito HTTP di un push: codice di stato e corpo.</summary>
    public sealed record SalesDocumentOutcome(int StatusCode, SalesDocumentResult Result);

    public interface ISalesDocumentService
    {
        /// <summary>Crea (o ritrova, se esiste già per lo stesso correlationId) il documento di vendita in SAP.</summary>
        Task<SalesDocumentOutcome> CreateAsync(SalesDocumentRequest request);

        /// <summary>Stesso percorso di <see cref="CreateAsync"/> fino al payload, senza scrivere nulla in SAP.</summary>
        Task<SalesDocumentPreview> PreviewAsync(SalesDocumentRequest request);

        /// <summary>
        /// Stato del documento creato per la correlazione (trovato, tipo, stato, annullato). Sola lettura: il
        /// portale la chiede prima di annullare un proprio documento già in SAP.
        /// </summary>
        Task<SalesDocumentState> GetStateAsync(string correlationId);
    }

    /// <summary>
    /// Push di un documento di vendita del portale (fattura o ordine, bozza o definitivo) nel Service Layer.
    ///
    /// <para><b>Idempotenza.</b> La chiave è il correlationId (Id della fattura del portale) in
    /// <c>U_AX360_InvId</c>, cercato su OINV, ORDR e sulle bozze ODRF di tipo 13/17. "Cerca, poi crea" non è
    /// atomico e in SAP non c'è un vincolo univoco sul campo: le richieste con lo stesso correlationId si
    /// serializzano con un lock nel processo. Se il documento esiste si restituisce QUELLO, con il suo tipo e
    /// il suo stato reali: una bozza non viene convertita in definitivo se il portale chiede il definitivo
    /// (il portale confronta l'eco e decide), e un documento annullato conta come esistente.</para>
    ///
    /// <para><b>Fail-closed sulla correlazione.</b> Se <c>U_AX360_InvId</c> manca sulla tabella di
    /// destinazione (o, per una bozza, su quella del tipo) il documento NON si crea: nascerebbe senza chiave,
    /// il primo retry lo duplicherebbe e il portale non lo aggancerebbe mai.</para>
    ///
    /// <para><b>Allegati.</b> Opt-in (<c>SapB1:SalesDocuments:Attachments:Enabled</c>), caricati prima del documento;
    /// un errore dell'allegato non annulla il documento e torna in <c>attachmentError</c>. Se SAP rifiuta il
    /// documento dopo l'upload, il nuovo tentativo riusa la voce già caricata (stesso nome di file).</para>
    /// </summary>
    public sealed class SalesDocumentService : ISalesDocumentService
    {
        // Statico: il servizio è Scoped, il lock deve valere per tutto il processo.
        private static readonly KeyedAsyncLock CorrelationLocks = new();

        private readonly IDbOdbcService _db;
        private readonly ISapB1ServiceLayerService _sl;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SalesDocumentService> _logger;

        public SalesDocumentService(
            IDbOdbcService db,
            ISapB1ServiceLayerService sl,
            IConfiguration configuration,
            ILogger<SalesDocumentService> logger)
        {
            _db = db;
            _sl = sl;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Le fatture DEFINITIVE sono documenti fiscali irreversibili (si stornano solo con una nota di
        /// credito): opt-in esplicito, chiave assente ⇒ disattivate. Stesso schema di <c>SapB1:Write:Enabled</c>:
        /// deploy.ps1 non copia appsettings.json, quindi un server non configurato resta nel default sicuro.
        /// Bozze e ordini (annullabili) non sono soggetti al flag.
        /// </summary>
        private bool AllowPostedInvoices =>
            bool.TryParse(_configuration["SapB1:SalesDocuments:AllowPostedInvoices"], out var v) && v;

        /// <summary>
        /// Allegati: opt-in esplicito, chiave assente o non booleana ⇒ spenti, come gli altri interruttori di
        /// scrittura (AllowPostedInvoices, Bootstrap). Una company copiata eredita OADP.AttachPath della
        /// produzione: con il default acceso un'istanza di test configurata a metà scriverebbe i PDF nella cartella
        /// allegati di produzione in silenzio, mentre con il default spento una produzione non configurata crea i
        /// documenti senza PDF e lo dice in <c>attachmentError</c>. <c>true</c> va scritto solo sull'istanza di
        /// produzione (o su una di test la cui cartella allegati è stata separata).
        /// </summary>
        internal static bool AttachmentsEnabledIn(IConfiguration configuration) =>
            bool.TryParse(configuration["SapB1:SalesDocuments:Attachments:Enabled"], out var v) && v;

        private bool AttachmentsEnabled => AttachmentsEnabledIn(_configuration);

        public async Task<SalesDocumentOutcome> CreateAsync(SalesDocumentRequest request)
        {
            var errors = SalesDocumentPayloadBuilder.Validate(request);
            if (errors.Count > 0)
                return Fail(400, string.Join(" ", errors));

            var target = SalesDocumentPayloadBuilder.ResolveTarget(request.DocumentKind, request.Posting)!;
            if (target.DocumentKind == SalesDocumentKinds.Invoice && !target.IsDraft && !AllowPostedInvoices)
                return Fail(403, "Fatture definitive disattivate su questa istanza (SapB1:SalesDocuments:AllowPostedInvoices=false): inviare come bozza.");

            var correlationId = request.CorrelationId!.Trim();
            using (await CorrelationLocks.AcquireAsync(correlationId))
            {
                var prepared = await PrepareAsync(request, target);
                if (prepared.Error != null)
                    return Fail(500, prepared.Error);

                if (prepared.Existing != null)
                {
                    _logger.LogInformation(
                        "Documento di vendita {CorrelationId}: già presente ({Kind}/{Status} DocEntry={DocEntry} in {Table}), nessun doppione.",
                        correlationId, prepared.Existing.DocumentKind, prepared.Existing.Status, prepared.Existing.DocEntry, prepared.Existing.Table);
                    var existing = SalesDocumentPayloadBuilder.FromExisting(prepared.Existing);
                    existing.Warnings.AddRange(prepared.Warnings);
                    return new SalesDocumentOutcome(200, existing);
                }

                var (attachmentEntry, attachmentError) = await UploadAttachmentsAsync(request, correlationId);

                var payload = SalesDocumentPayloadBuilder.Build(request, target, prepared.Schema!, prepared.LineUoms, attachmentEntry);
                var response = await _sl.CreateSalesDocumentAsync(target.Entity, payload);
                if (!response.IsSuccess)
                {
                    var message = ServiceLayerErrors.ExtractMessage(response.Body);
                    _logger.LogError("Creazione {Entity} ({Kind}/{Status}) per {CorrelationId} rifiutata da SAP ({StatusCode}): {Body}",
                        target.Entity, target.DocumentKind, target.Posting, correlationId, response.StatusCode, response.Body);
                    var failed = new SalesDocumentResult
                    {
                        Success = false,
                        DocumentKind = target.DocumentKind,
                        Status = target.Posting,
                        ErrorMessage = $"SAP B1 ha rifiutato il documento ({response.StatusCode}): {message}",
                        AttachmentEntry = attachmentEntry,
                        AttachmentError = attachmentError,
                    };
                    failed.Warnings.AddRange(prepared.Warnings);
                    return new SalesDocumentOutcome(502, failed);
                }

                var result = SalesDocumentPayloadBuilder.ParseCreated(SalesDocumentPayloadBuilder.ParseBody(response.Body), target);
                if (result == null)
                {
                    // Creato ma senza DocEntry nella risposta: si rilegge per correlationId invece di rispondere
                    // "fallito" (un retry del portale troverà comunque il documento).
                    var reread = SalesDocumentPayloadBuilder.SelectExisting(
                        await _db.FindSalesDocumentsByCorrelationIdAsync(correlationId, prepared.CorrelationTables), target.DocumentKind);
                    result = reread != null
                        ? SalesDocumentPayloadBuilder.FromExisting(reread)
                        : new SalesDocumentResult { Success = true, DocumentKind = target.DocumentKind, Status = target.Posting, ObjectType = target.ObjectType };
                    result.AlreadyExisted = false;
                }
                else if (result.DocTotal == null || result.VatSum == null)
                {
                    await RereadTotalsAsync(target, result);
                }

                result.AttachmentEntry = attachmentEntry;
                result.AttachmentError = attachmentError;
                result.Warnings.AddRange(prepared.Warnings);

                _logger.LogInformation(
                    "Documento di vendita creato: {Entity} {Kind}/{Status} DocEntry={DocEntry} DocNum={DocNum} Totale={DocTotal} IVA={VatSum} CorrelationId={CorrelationId} Allegato={AttachmentEntry}",
                    target.Entity, target.DocumentKind, target.Posting, result.DocEntry, result.DocNum, result.DocTotal, result.VatSum, correlationId, attachmentEntry);
                return new SalesDocumentOutcome(201, result);
            }
        }

        public async Task<SalesDocumentPreview> PreviewAsync(SalesDocumentRequest request)
        {
            var preview = new SalesDocumentPreview();
            preview.Errors.AddRange(SalesDocumentPayloadBuilder.Validate(request));
            if (preview.Errors.Count > 0) return preview;

            var target = SalesDocumentPayloadBuilder.ResolveTarget(request.DocumentKind, request.Posting)!;
            if (target.DocumentKind == SalesDocumentKinds.Invoice && !target.IsDraft && !AllowPostedInvoices)
                preview.Warnings.Add("Fatture definitive disattivate su questa istanza (SapB1:SalesDocuments:AllowPostedInvoices=false): il POST risponderebbe 403.");

            var prepared = await PrepareAsync(request, target);
            if (prepared.Error != null)
            {
                preview.Errors.Add(prepared.Error);
                return preview;
            }

            preview.Entity = target.Entity;
            preview.Existing = prepared.Existing != null ? SalesDocumentPayloadBuilder.FromExisting(prepared.Existing) : null;
            preview.Payload = SalesDocumentPayloadBuilder.Build(request, target, prepared.Schema!, prepared.LineUoms);
            preview.Warnings.AddRange(prepared.Warnings);
            if (request.Attachments?.Count > 0 && !AttachmentsEnabled)
                preview.Warnings.Add("Allegati disattivati da configurazione (SapB1:SalesDocuments:Attachments:Enabled=false).");
            return preview;
        }

        public async Task<SalesDocumentState> GetStateAsync(string correlationId)
        {
            var udfInvId = Ax360Udf.Col(Ax360Udf.InvId);
            var tables = new List<string>();
            foreach (var table in DbOdbcService.CorrelationLookupTables)
                if ((await _db.GetUserFieldColumnsAsync(table)).Contains(udfInvId)) tables.Add(table);
            // Nessuna tabella con il campo di correlazione: non si può dire "non c'è" (il portale libererebbe le
            // ore di un documento forse esistente). Meglio un errore esplicito.
            if (tables.Count == 0)
                throw new InvalidOperationException($"Il campo utente {udfInvId} non esiste su nessuna tabella dei documenti di vendita: stato non verificabile.");

            var existing = SalesDocumentPayloadBuilder.SelectExisting(
                await _db.FindSalesDocumentsByCorrelationIdAsync(correlationId.Trim(), tables));
            return SalesDocumentPayloadBuilder.ToState(existing);
        }

        // ─────────────────────────────────────────────────────────────────────

        private sealed class Prepared
        {
            public string? Error { get; set; }
            public ExistingSalesDocument? Existing { get; set; }
            public IReadOnlyCollection<string> CorrelationTables { get; set; } = Array.Empty<string>();
            public SalesDocumentSchema? Schema { get; set; }
            public IReadOnlyList<LineUom>? LineUoms { get; set; }
            public List<string> Warnings { get; } = new();
        }

        /// <summary>
        /// Tutto ciò che serve prima di scrivere: campi di correlazione, documento esistente, metadati della
        /// destinazione, unità di misura. Condiviso fra push e anteprima, così l'anteprima mostra esattamente
        /// ciò che il push invierebbe.
        /// </summary>
        private async Task<Prepared> PrepareAsync(SalesDocumentRequest request, SalesDocumentTarget target)
        {
            var prepared = new Prepared();
            var udfInvId = Ax360Udf.Col(Ax360Udf.InvId);

            // 1) Campo di correlazione: obbligatorio sulle tabelle della destinazione, facoltativo altrove
            //    (una tabella senza il campo semplicemente non entra nella ricerca per correlationId).
            var required = SalesDocumentPayloadBuilder.RequiredCorrelationTables(target);
            var withUdf = new List<string>();
            foreach (var table in DbOdbcService.CorrelationLookupTables)
            {
                var fields = await _db.GetUserFieldColumnsAsync(table);
                if (!fields.Contains(udfInvId) && required.Contains(table))
                    fields = await _db.GetUserFieldColumnsAsync(table, forceRefresh: true); // appena creato dal bootstrap?
                if (fields.Contains(udfInvId)) withUdf.Add(table);
                else if (required.Contains(table))
                {
                    prepared.Error = $"Il campo utente {udfInvId} non esiste sulla tabella {table}: il documento non si crea senza correlazione. " +
                                     "Avviare il servizio con SapB1:Bootstrap:UserFields:Enabled=true per crearlo, oppure crearlo in SAP.";
                    return prepared;
                }
            }
            prepared.CorrelationTables = withUdf;

            // 2) Idempotenza: a parità di validità vince il documento del tipo chiesto (vedi SelectExisting).
            prepared.Existing = SalesDocumentPayloadBuilder.SelectExisting(
                await _db.FindSalesDocumentsByCorrelationIdAsync(request.CorrelationId!.Trim(), withUdf), target.DocumentKind);
            if (prepared.Existing != null) return prepared;

            // 3) Metadati della destinazione: campi utente di testata e lunghezze per troncare.
            prepared.Schema = new SalesDocumentSchema
            {
                HeaderUserFields = await _db.GetUserFieldColumnsAsync(target.HeaderTable),
                HeaderLengths = await _db.GetColumnLengthsAsync(target.HeaderTable),
                LineLengths = await _db.GetColumnLengthsAsync(target.LineTable),
            };
            foreach (var udf in new[] { SalesDocumentPayloadBuilder.UdfOrderNumber, SalesDocumentPayloadBuilder.UdfOrderDate, SalesDocumentPayloadBuilder.UdfCig, SalesDocumentPayloadBuilder.UdfCup })
            {
                if (!prepared.Schema.HeaderUserFields.Contains(udf) && HasReferenceValue(request, udf))
                    prepared.Warnings.Add($"Il campo {udf} non esiste su {target.HeaderTable}: il riferimento del cliente non è stato riportato.");
            }

            // 4) Unità di misura per riga.
            var requestLines = request.Lines!;
            var uomCodes = requestLines
                .Select(l => l.UnitOfMeasureCode?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
            if (uomCodes.Count > 0)
            {
                var itemCodes = requestLines
                    .Where(l => !string.IsNullOrWhiteSpace(l.UnitOfMeasureCode))
                    .Select(l => l.ItemCode!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var resolution = await _db.ResolveUnitsOfMeasureAsync(itemCodes, uomCodes);
                var lineUoms = new List<LineUom>(requestLines.Count);
                foreach (var line in requestLines)
                {
                    var itemCode = line.ItemCode!.Trim();
                    var code = line.UnitOfMeasureCode?.Trim();
                    int? entry = code != null && resolution.UomEntryByCode.TryGetValue(code, out var e) ? e : null;
                    resolution.Items.TryGetValue(itemCode, out var item);
                    var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom(itemCode, code, entry, item);
                    lineUoms.Add(uom);
                    if (warning != null && !prepared.Warnings.Contains(warning)) prepared.Warnings.Add(warning);
                }
                prepared.LineUoms = lineUoms;
            }

            return prepared;
        }

        private static bool HasReferenceValue(SalesDocumentRequest request, string udf)
        {
            var r = request.CustomerReference;
            if (r == null) return false;
            return udf switch
            {
                SalesDocumentPayloadBuilder.UdfOrderNumber => !string.IsNullOrWhiteSpace(r.OrderNumber),
                SalesDocumentPayloadBuilder.UdfOrderDate => r.OrderDate != null,
                SalesDocumentPayloadBuilder.UdfCig => !string.IsNullOrWhiteSpace(r.Cig),
                SalesDocumentPayloadBuilder.UdfCup => !string.IsNullOrWhiteSpace(r.Cup),
                _ => false,
            };
        }

        /// <summary>Carica gli allegati; mai fatale per il documento. Restituisce (AbsoluteEntry, errore).</summary>
        private async Task<(int? Entry, string? Error)> UploadAttachmentsAsync(SalesDocumentRequest request, string correlationId)
        {
            if (request.Attachments == null || request.Attachments.Count == 0) return (null, null);
            if (!AttachmentsEnabled)
                return (null, "Allegati disattivati da configurazione (SapB1:SalesDocuments:Attachments:Enabled=false).");

            var files = new List<ServiceLayerFile>();
            foreach (var a in request.Attachments)
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(a?.ContentBase64 ?? string.Empty);
                }
                catch (FormatException)
                {
                    return (null, $"Allegato '{a?.FileName}': contenuto non in base64 valido.");
                }
                if (bytes.Length == 0)
                    return (null, $"Allegato '{a?.FileName}': contenuto vuoto.");
                files.Add(new ServiceLayerFile(
                    SalesDocumentPayloadBuilder.SanitizeAttachmentFileName(a!.FileName, correlationId),
                    string.IsNullOrWhiteSpace(a.ContentType) ? "application/pdf" : a.ContentType.Trim(),
                    bytes));
            }

            // Un tentativo precedente dello STESSO documento (rifiutato da SAP dopo l'upload) ha già caricato questi
            // file con lo stesso nome: si riusa quella voce invece di ricaricare (il nome già presente farebbe fallire
            // l'upload e il documento nascerebbe senza PDF). La ricerca è un'ottimizzazione: se fallisce, si carica.
            try
            {
                var names = files.Select(f => SalesDocumentPayloadBuilder.SplitFileName(f.FileName).Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var reusable = SalesDocumentPayloadBuilder.SelectReusableAttachmentEntry(
                    await _db.FindAttachmentsByFileNamesAsync(names), files.Select(f => f.FileName).ToList());
                if (reusable != null)
                {
                    _logger.LogInformation("Allegati per {CorrelationId} già caricati da un tentativo precedente: riuso di Attachments2({Entry}).", correlationId, reusable);
                    return (reusable, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ricerca degli allegati già caricati per {CorrelationId} non riuscita: si caricano di nuovo.", correlationId);
            }

            try
            {
                var response = await _sl.UploadAttachmentsAsync(files);
                if (!response.IsSuccess)
                {
                    _logger.LogWarning("Upload allegati per {CorrelationId} rifiutato ({StatusCode}): {Body}", correlationId, response.StatusCode, response.Body);
                    return (null, $"Allegato non caricato in SAP ({response.StatusCode}): {ServiceLayerErrors.ExtractMessage(response.Body)}");
                }
                var entry = SalesDocumentPayloadBuilder.ReadInt(SalesDocumentPayloadBuilder.ParseBody(response.Body)?["AbsoluteEntry"]);
                return entry != null
                    ? (entry, null)
                    : (null, "Allegato caricato ma SAP non ha restituito AbsoluteEntry: documento creato senza allegato.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload allegati per {CorrelationId} fallito.", correlationId);
                return (null, $"Allegato non caricato: {ex.Message}");
            }
        }

        private async Task RereadTotalsAsync(SalesDocumentTarget target, SalesDocumentResult result)
        {
            try
            {
                var response = await _sl.GetSalesDocumentAsync(target.Entity, result.DocEntry!.Value);
                if (!response.IsSuccess) return;
                var reread = SalesDocumentPayloadBuilder.ParseCreated(SalesDocumentPayloadBuilder.ParseBody(response.Body), target);
                if (reread == null) return;
                result.DocNum ??= reread.DocNum;
                result.DocTotal ??= reread.DocTotal;
                result.VatSum ??= reread.VatSum;
                result.DocDueDate ??= reread.DocDueDate;
            }
            catch (Exception ex)
            {
                // Il documento c'è: una rilettura mancata non deve trasformare un successo in un errore.
                _logger.LogWarning(ex, "Rilettura totali di {Entity}({DocEntry}) non riuscita.", target.Entity, result.DocEntry);
            }
        }

        private static SalesDocumentOutcome Fail(int statusCode, string message)
            => new(statusCode, new SalesDocumentResult { Success = false, ErrorMessage = message });
    }
}

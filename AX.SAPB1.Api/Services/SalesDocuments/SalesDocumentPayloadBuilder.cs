using System.Globalization;
using System.Text;
using AX.SAPB1.Api.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AX.SAPB1.Api.Services.SalesDocuments
{
    /// <summary>
    /// Traduzione PURA del contratto ERP-neutro dei documenti di vendita nel payload del Service Layer SAP B1,
    /// e ritorno. Niente HTTP, niente ODBC: tutto ciò che dipende dallo schema arriva come parametro
    /// (<see cref="SalesDocumentSchema"/>, unità di misura risolte), così ogni regola è verificabile con un
    /// test senza SAP.
    ///
    /// <para><b>Differenze volute rispetto al vecchio <c>CreateInvoiceDraftAsync</c></b> (che resta invariato
    /// per <c>POST /api/invoices</c>):</para>
    /// <list type="bullet">
    /// <item>nessun ripiego su <c>SapB1:TimeAndMaterialsItemCode</c>: una riga senza articolo è un errore
    /// (400), perché il ripiego mascherava il controllo "articolo mancante" del portale;</item>
    /// <item>niente <c>LineTotal</c>: con ore frazionarie × tariffa SAP ricalcolava o arrotondava a modo suo.
    /// Si mandano quantità e prezzo, e i totali si rileggono dalla risposta;</item>
    /// <item><c>DocDueDate</c> solo se valorizzata: il vecchio push la forzava alla data documento,
    /// scavalcando le condizioni di pagamento del cliente;</item>
    /// <item>sconto esplicitamente a zero su testata e righe: il prezzo lo decide il portale (tariffa già
    /// scontata). Senza, uno sconto di anagrafica del cliente cambierebbe in silenzio i totali in SAP.</item>
    /// </list>
    /// </summary>
    public static class SalesDocumentPayloadBuilder
    {
        // UDF di fatturazione elettronica MTF: di TESTATA documento (creati su OINV da
        // MTF.FatturazioneElettronica DBSetup: ODA/CIG/CUP alfanumerici 250, DTORD di tipo data). Si inviano
        // solo se esistono sulla tabella di destinazione: un campo utente sconosciuto fa rifiutare il documento.
        internal const string UdfOrderNumber = "U_MTF_FE_ODA";
        internal const string UdfOrderDate = "U_MTF_FE_DTORD";
        internal const string UdfCig = "U_MTF_FE_CIG";
        internal const string UdfCup = "U_MTF_FE_CUP";

        /// <summary>Lunghezza del valore di <c>U_AX360_InvId</c> (UDF creato a 50 dal bootstrap).</summary>
        internal const int CorrelationIdMaxLength = 50;

        /// <summary>
        /// Lunghezze di ripiego quando la lettura di <c>SYS.TABLE_COLUMNS</c> non è disponibile. Sono le
        /// MINIME fra le versioni SAP B1 note, così il ripiego tronca di più ma non fa mai rifiutare il
        /// documento: <c>Dscription</c> è 100 in SAP B1 9.x e 200 in 10.0; <c>NumAtCard</c> 100;
        /// <c>Comments</c> 254; <c>OPRJ.PrjName</c> 100; UDF AX360 50 (bootstrap); UDF FE 250 (DBSetup FE).
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, int> FallbackLengths =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dscription"] = 100,
                ["NumAtCard"] = 100,
                ["Comments"] = 254,
                ["PrjName"] = 100,
                ["unitMsr"] = 100,
                [Ax360Udf.Col(Ax360Udf.InvNum)] = 50,
                [UdfOrderNumber] = 250,
                [UdfCig] = 250,
                [UdfCup] = 250,
            };

        // ─────────────────────────────────────────────────────────────────────
        // Destinazione e validazione
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Mappa (tipo, stato) → entità Service Layer e tabelle SAP. Null se la combinazione non è
        /// supportata. Le bozze stanno tutte in ODRF/DRF1, distinte dal <c>DocObjectCode</c>.
        /// </summary>
        public static SalesDocumentTarget? ResolveTarget(string? documentKind, string? posting)
        {
            var kind = documentKind?.Trim().ToLowerInvariant();
            var post = posting?.Trim().ToLowerInvariant();
            return (kind, post) switch
            {
                (SalesDocumentKinds.Invoice, SalesDocumentKinds.Draft) =>
                    new(SalesDocumentKinds.Invoice, SalesDocumentKinds.Draft, "Drafts", "oInvoices", "ODRF", "DRF1", SalesDocumentKinds.ObjectTypeDraft, "OINV"),
                (SalesDocumentKinds.Order, SalesDocumentKinds.Draft) =>
                    new(SalesDocumentKinds.Order, SalesDocumentKinds.Draft, "Drafts", "oOrders", "ODRF", "DRF1", SalesDocumentKinds.ObjectTypeDraft, "ORDR"),
                (SalesDocumentKinds.Invoice, SalesDocumentKinds.Posted) =>
                    new(SalesDocumentKinds.Invoice, SalesDocumentKinds.Posted, "Invoices", null, "OINV", "INV1", SalesDocumentKinds.ObjectTypeInvoice, "OINV"),
                (SalesDocumentKinds.Order, SalesDocumentKinds.Posted) =>
                    new(SalesDocumentKinds.Order, SalesDocumentKinds.Posted, "Orders", null, "ORDR", "RDR1", SalesDocumentKinds.ObjectTypeOrder, "ORDR"),
                _ => null,
            };
        }

        /// <summary>
        /// Tabelle su cui <c>U_AX360_InvId</c> DEVE esistere prima di creare il documento. Senza il campo il
        /// documento nascerebbe senza correlazione: l'idempotenza non lo ritroverebbe (doppione al primo
        /// retry) e il portale non lo aggancerebbe mai. Per una bozza serve anche la tabella del tipo
        /// (OINV/ORDR): è lì che il valore deve propagarsi quando la bozza viene confermata.
        /// </summary>
        public static IReadOnlyList<string> RequiredCorrelationTables(SalesDocumentTarget target)
            => target.IsDraft
                ? new[] { target.KindHeaderTable, target.HeaderTable }
                : new[] { target.HeaderTable };

        /// <summary>Errori bloccanti del contratto (400). Lista vuota = richiesta valida.</summary>
        public static List<string> Validate(SalesDocumentRequest? request)
        {
            var errors = new List<string>();
            if (request == null)
            {
                errors.Add("Payload del documento mancante.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationId))
                errors.Add("correlationId obbligatorio (chiave di idempotenza).");
            else if (request.CorrelationId.Trim().Length > CorrelationIdMaxLength)
                errors.Add($"correlationId troppo lungo: massimo {CorrelationIdMaxLength} caratteri.");

            if (ResolveTarget(request.DocumentKind, request.Posting) == null)
                errors.Add("documentKind deve essere 'invoice' oppure 'order' e posting 'draft' oppure 'posted'.");

            if (string.IsNullOrWhiteSpace(request.CardCode))
                errors.Add("cardCode obbligatorio.");

            if (request.DocDate == null)
                errors.Add("docDate obbligatoria.");

            if (request.Lines == null || request.Lines.Count == 0)
            {
                errors.Add("Il documento non ha righe.");
            }
            else
            {
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var line = request.Lines[i];
                    var n = i + 1;
                    if (line == null)
                    {
                        errors.Add($"Riga {n}: vuota.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(line.ItemCode))
                        errors.Add($"Riga {n}: itemCode obbligatorio (nessun articolo di ripiego).");
                    if (line.Quantity <= 0)
                        errors.Add($"Riga {n}: quantity deve essere maggiore di zero.");
                }
            }

            return errors;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Payload
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Payload del documento per il Service Layer. <paramref name="lineUoms"/> è parallelo a
        /// <c>request.Lines</c> (null = nessuna unità di misura da inviare). Le chiavi con valore nullo non
        /// vengono proprio inserite: il Service Layer applica i suoi default.
        /// </summary>
        public static Dictionary<string, object?> Build(
            SalesDocumentRequest request,
            SalesDocumentTarget target,
            SalesDocumentSchema schema,
            IReadOnlyList<LineUom>? lineUoms = null,
            int? attachmentEntry = null)
        {
            var docDate = FormatDate(request.DocDate);
            var headerProject = NullIfBlank(request.ProjectCode);
            var reference = request.CustomerReference;
            var orderNumber = NullIfBlank(reference?.OrderNumber);

            var header = new Dictionary<string, object?>();
            if (target.DocObjectCode != null) header["DocObjectCode"] = target.DocObjectCode;
            header["DocType"] = "dDocument_Items";
            header["CardCode"] = request.CardCode!.Trim();
            header["DocDate"] = docDate;
            header["TaxDate"] = docDate;
            Put(header, "DocDueDate", FormatDate(request.DueDate));
            Put(header, "Comments", Truncate(NullIfBlank(request.Comments), LengthOf(schema.HeaderLengths, "Comments")));
            Put(header, "Project", headerProject);
            Put(header, "NumAtCard", Truncate(orderNumber, LengthOf(schema.HeaderLengths, "NumAtCard")));
            header["DiscountPercent"] = 0m;

            // Riferimenti del cliente per la fatturazione elettronica: solo se il campo esiste sulla tabella.
            PutUdf(header, schema, UdfOrderNumber, orderNumber);
            if (schema.HeaderUserFields.Contains(UdfOrderDate))
                Put(header, UdfOrderDate, FormatDate(reference?.OrderDate));
            PutUdf(header, schema, UdfCig, NullIfBlank(reference?.Cig));
            PutUdf(header, schema, UdfCup, NullIfBlank(reference?.Cup));

            // Correlazione AX.360. La presenza di U_AX360_InvId è verificata PRIMA (RequiredCorrelationTables):
            // qui lo si scrive sempre, perché un documento senza correlazione non deve poter nascere.
            header[Ax360Udf.Col(Ax360Udf.InvId)] = request.CorrelationId!.Trim();
            PutUdf(header, schema, Ax360Udf.Col(Ax360Udf.InvNum), NullIfBlank(request.PortalNumber));

            if (attachmentEntry.HasValue) header["AttachmentEntry"] = attachmentEntry.Value;

            var descriptionLength = LengthOf(schema.LineLengths, "Dscription");
            var measureUnitLength = LengthOf(schema.LineLengths, "unitMsr");
            var lines = new List<Dictionary<string, object?>>();
            var requestLines = request.Lines ?? new List<SalesDocumentLineRequest>();
            for (var i = 0; i < requestLines.Count; i++)
            {
                var l = requestLines[i];
                var line = new Dictionary<string, object?>
                {
                    ["ItemCode"] = l.ItemCode!.Trim(),
                };
                Put(line, "ItemDescription", Truncate(NullIfBlank(l.Description), descriptionLength));
                line["Quantity"] = l.Quantity;
                line["UnitPrice"] = l.UnitPrice;
                line["DiscountPercent"] = 0m;

                var uom = lineUoms != null && i < lineUoms.Count ? lineUoms[i] : default;
                if (uom.UoMEntry.HasValue) line["UoMEntry"] = uom.UoMEntry.Value;
                Put(line, "MeasureUnit", Truncate(uom.MeasureUnit, measureUnitLength));

                // Il progetto di riga è quello che arriva su JDT1 (ricavo per commessa). Via DI/Service Layer la
                // testata non si propaga alle righe: una riga senza progetto eredita quello di testata.
                Put(line, "ProjectCode", NullIfBlank(l.ProjectCode) ?? headerProject);
                Put(line, "CostingCode2", NullIfBlank(l.CostingCode2));
                Put(line, "CostingCode3", NullIfBlank(l.CostingCode3));
                Put(line, "VatGroup", NullIfBlank(l.VatGroup));
                lines.Add(line);
            }
            header["DocumentLines"] = lines;
            return header;
        }

        /// <summary>
        /// Decide come comunicare l'unità di misura di una riga. Gli articoli con gruppo UdM (UgpEntry ≠ -1)
        /// accettano solo un <c>UoMEntry</c> che appartiene al loro gruppo: mandarne uno estraneo fa rifiutare
        /// l'intero documento, quindi in quel caso non si manda nulla e si avvisa (gli importi restano giusti:
        /// quantità e prezzo sono espliciti). Gli articoli "manuali" (UgpEntry = -1) non hanno UoMEntry: si
        /// scrive il codice come testo in <c>MeasureUnit</c>.
        /// </summary>
        public static (LineUom Uom, string? Warning) ResolveLineUom(
            string? itemCode, string? uomCode, int? uomEntryForCode, ItemUomInfo? item)
        {
            var code = NullIfBlank(uomCode);
            if (code == null) return (default, null);

            // Articolo sconosciuto: sarà SAP a rifiutare la riga, con il suo messaggio.
            if (item == null) return (new LineUom(uomEntryForCode, null), null);

            if (item.UgpEntry == -1) return (new LineUom(null, code), null);

            if (uomEntryForCode == null)
                return (default, $"Unità di misura '{code}' inesistente in SAP (OUOM): la riga '{itemCode}' è inviata senza unità di misura.");

            if (item.GroupUomEntries.Contains(uomEntryForCode.Value))
                return (new LineUom(uomEntryForCode, null), null);

            return (default, $"L'unità di misura '{code}' non appartiene al gruppo UdM dell'articolo '{itemCode}': SAP userà l'unità di vendita predefinita dell'articolo (importi invariati).");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Risposta
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Esito dal corpo della risposta di creazione del Service Layer (l'entità creata, con i totali
        /// calcolati da SAP). Null se manca il DocEntry: senza non c'è un documento da restituire.
        /// </summary>
        public static SalesDocumentResult? ParseCreated(JObject? body, SalesDocumentTarget target)
        {
            var docEntry = ReadInt(body?["DocEntry"]);
            if (docEntry == null) return null;

            return new SalesDocumentResult
            {
                Success = true,
                DocumentKind = target.DocumentKind,
                Status = target.Posting,
                ObjectType = target.ObjectType,
                DocEntry = docEntry,
                DocNum = ReadInt(body?["DocNum"]),
                DocTotal = ReadDecimal(body?["DocTotal"]),
                VatSum = ReadDecimal(body?["VatSum"]),
                DocDueDate = ReadDate(body?["DocDueDate"]),
            };
        }

        /// <summary>Esito per un documento già esistente (idempotenza): sempre i dati del documento REALE.</summary>
        public static SalesDocumentResult FromExisting(ExistingSalesDocument existing) => new()
        {
            Success = true,
            AlreadyExisted = true,
            DocumentKind = existing.DocumentKind,
            Status = existing.Status,
            ObjectType = ObjectTypeOf(existing.DocumentKind, existing.Status),
            DocEntry = existing.DocEntry,
            DocNum = existing.DocNum,
            DocTotal = existing.DocTotal,
            VatSum = existing.VatSum,
            DocDueDate = existing.DocDueDate,
        };

        public static string ObjectTypeOf(string documentKind, string status)
            => status == SalesDocumentKinds.Draft
                ? SalesDocumentKinds.ObjectTypeDraft
                : documentKind == SalesDocumentKinds.Order ? SalesDocumentKinds.ObjectTypeOrder : SalesDocumentKinds.ObjectTypeInvoice;

        /// <summary>
        /// Fra più documenti con lo stesso correlationId sceglie quello da restituire, in modo deterministico:
        /// prima i validi (<c>CANCELED = 'N'</c>), poi gli annullati, poi i documenti di annullamento; a parità
        /// quello del tipo chiesto, poi il definitivo prima della bozza (una bozza confermata può sopravvivere
        /// accanto al definitivo), poi DocEntry e tipo.
        /// <para>Il tipo chiesto conta perché gli UDF di testata si copiano da un documento all'altro: la
        /// fattura tratta in SAP da un ordine del portale eredita il suo <c>U_AX360_InvId</c>, e un nuovo push
        /// dell'ordine deve ritrovare l'ORDINE, non la fattura nata da lui.</para>
        /// <para>Un documento annullato conta comunque come esistente: per rifatturare dopo un annullamento il
        /// portale deve emettere un documento nuovo (nuovo Id), mai riusare la stessa chiave.</para>
        /// </summary>
        public static ExistingSalesDocument? SelectExisting(IEnumerable<ExistingSalesDocument> candidates, string? preferredKind = null)
            => candidates
                .OrderBy(d => d.Canceled switch { "N" => 0, "Y" => 1, _ => 2 })
                .ThenBy(d => preferredKind != null && d.DocumentKind == preferredKind ? 0 : 1)
                .ThenBy(d => d.Status == SalesDocumentKinds.Posted ? 0 : 1)
                .ThenBy(d => d.DocEntry)
                .ThenBy(d => d.DocumentKind, StringComparer.Ordinal)
                .FirstOrDefault();

        // ─────────────────────────────────────────────────────────────────────
        // Allegati
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Nome file per la cartella allegati di SAP: solo ASCII (niente accenti nell'header multipart), niente
        /// percorsi, e un suffisso dal correlationId. Il suffisso non è estetica: la cartella allegati è
        /// condivisa da tutta la company e un nome già presente fa fallire l'upload — "Cliente - Attivita
        /// settembre 2026.pdf" si ripete ogni volta che lo stesso documento viene ritentato.
        /// </summary>
        public static string SanitizeAttachmentFileName(string? fileName, string correlationId)
        {
            var raw = (fileName ?? string.Empty).Replace('\\', '/');
            raw = raw.Contains('/') ? raw[(raw.LastIndexOf('/') + 1)..] : raw;

            var dot = raw.LastIndexOf('.');
            var name = dot > 0 ? raw[..dot] : raw;
            var ext = dot > 0 ? raw[dot..] : ".pdf";

            name = ToSafeAscii(name).Trim(' ', '.', '_');
            ext = "." + ToSafeAscii(ext.TrimStart('.'));
            if (ext == ".") ext = ".pdf";
            if (name.Length == 0) name = "allegato";
            if (name.Length > 80) name = name[..80].TrimEnd(' ', '.', '_');

            var suffix = new string(correlationId.Where(char.IsLetterOrDigit).Take(8).ToArray());
            return suffix.Length == 0 ? name + ext : $"{name}_{suffix}{ext}";
        }

        private static string ToSafeAscii(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')') ? c : '_');
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tronca a <paramref name="maxLength"/> caratteri senza spezzare una coppia surrogata. Null ⇒ nessun
        /// limite noto. Le colonne SAP non hanno vincoli applicati dal servizio: senza troncare, un testo
        /// lungo farebbe rifiutare l'intero documento dal Service Layer.
        /// </summary>
        public static string? Truncate(string? value, int? maxLength)
        {
            if (value == null || maxLength == null || maxLength <= 0 || value.Length <= maxLength) return value;
            var cut = maxLength.Value;
            if (char.IsHighSurrogate(value[cut - 1])) cut--;
            return value[..cut];
        }

        /// <summary>Lunghezza letta dallo schema, altrimenti quella di ripiego documentata, altrimenti nessun limite.</summary>
        public static int? LengthOf(IReadOnlyDictionary<string, int> lengths, string column)
        {
            if (lengths.TryGetValue(column, out var len) && len > 0) return len;
            return FallbackLengths.TryGetValue(column, out var fallback) ? fallback : null;
        }

        private static void PutUdf(Dictionary<string, object?> header, SalesDocumentSchema schema, string column, string? value)
        {
            if (value == null || !schema.HeaderUserFields.Contains(column)) return;
            header[column] = Truncate(value, LengthOf(schema.HeaderLengths, column));
        }

        private static void Put(Dictionary<string, object?> target, string key, object? value)
        {
            if (value != null) target[key] = value;
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? FormatDate(DateTime? value)
            => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// Legge il corpo JSON del Service Layer lasciando le date come testo (<see cref="DateParseHandling.None"/>):
        /// Newtonsoft altrimenti le convertirebbe nel fuso del server, e una data con offset slitterebbe di un giorno.
        /// I numeri decimali restano <c>decimal</c> (niente passaggio da <c>double</c> per i totali).
        /// </summary>
        public static JObject? ParseBody(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
                return JToken.ReadFrom(reader) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static int? ReadInt(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        internal static decimal? ReadDecimal(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type is JTokenType.Integer or JTokenType.Float) return token.Value<decimal>();
            return decimal.TryParse(token.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        /// <summary>
        /// Data SAP (solo giorno): "2026-10-30", "2026-10-30T00:00:00" o "2026-10-30T00:00:00Z". Mai convertita
        /// nel fuso del server: una data di scadenza non deve slittare di un giorno per colpa dell'offset.
        /// </summary>
        internal static DateTime? ReadDate(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Date)
            {
                var d = token.Value<DateTime>();
                return DateTime.SpecifyKind(d.Date, DateTimeKind.Unspecified);
            }
            var text = token.ToString().Trim();
            if (text.Length >= 10 && DateTime.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                return parsed;
            return null;
        }
    }
}

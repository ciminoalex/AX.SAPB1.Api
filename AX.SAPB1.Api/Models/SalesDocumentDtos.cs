namespace AX.SAPB1.Api.Models
{
    // ──────────────────────────────────────────────────────────────────────────
    // Contratto dei documenti di vendita (fattura/ordine, bozza/definitivo) spinti dal portale AX.360.
    // JSON in camelCase: i nomi combaciano con i gemelli del portale (specifica «Fatturazione dal portale»,
    // §4). Tutto ciò che qui è ERP-neutro resta tale: la traduzione nei nomi del Service Layer SAP B1 vive
    // in Services/SalesDocuments/SalesDocumentPayloadBuilder.cs, non nel contratto.
    // Il vecchio POST /api/invoices (ErpInvoicePushDto) resta INVARIATO: questo è un contratto separato.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Richiesta di <c>POST /api/sales-documents</c>. I campi obbligatori sono comunque dichiarati nullable:
    /// con i nullable reference types MVC renderebbe "required" ogni stringa non nullable e risponderebbe con un
    /// ProblemDetails generico prima del servizio. Così la validazione è una sola
    /// (<c>SalesDocumentPayloadBuilder.Validate</c>) e l'errore torna sempre in <c>errorMessage</c>.
    /// </summary>
    public class SalesDocumentRequest
    {
        /// <summary>Chiave di idempotenza: l'Id della fattura del portale. Finisce in <c>U_AX360_InvId</c>.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>Numero leggibile del portale (es. "FT-2026-0001"). Finisce in <c>U_AX360_InvNum</c>.</summary>
        public string? PortalNumber { get; set; }

        /// <summary>"invoice" (fattura) oppure "order" (ordine cliente).</summary>
        public string? DocumentKind { get; set; }

        /// <summary>"draft" (bozza) oppure "posted" (documento definitivo).</summary>
        public string? Posting { get; set; }

        public string? CardCode { get; set; }

        /// <summary>Data documento; è anche la data di riferimento fiscale (<c>TaxDate</c>).</summary>
        public DateTime? DocDate { get; set; }

        /// <summary>Scadenza. Null ⇒ non inviata: la calcola SAP dalle condizioni di pagamento del cliente.</summary>
        public DateTime? DueDate { get; set; }

        public string? Comments { get; set; }

        /// <summary>Progetto contabile di testata (OPRJ); è anche il default delle righe che non ne indicano uno.</summary>
        public string? ProjectCode { get; set; }

        public SalesDocumentCustomerReference? CustomerReference { get; set; }

        public List<SalesDocumentLineRequest>? Lines { get; set; } = new();

        public List<SalesDocumentAttachment>? Attachments { get; set; } = new();
    }

    /// <summary>Riferimenti del cliente: ordine/ODA, data ordine, CIG, CUP (fatturazione elettronica).</summary>
    public class SalesDocumentCustomerReference
    {
        public string? OrderNumber { get; set; }
        public DateTime? OrderDate { get; set; }
        public string? Cig { get; set; }
        public string? Cup { get; set; }
    }

    public class SalesDocumentLineRequest
    {
        /// <summary>Articolo SAP. Obbligatorio: nessun ripiego su un articolo di default.</summary>
        public string? ItemCode { get; set; }

        public string? Description { get; set; }

        /// <summary>Quantità (ore per le righe T&amp;M). Deve essere &gt; 0.</summary>
        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        /// <summary>Codice unità di misura (<c>OUOM.UomCode</c>, es. "HH"). Facoltativo.</summary>
        public string? UnitOfMeasureCode { get; set; }

        public string? ProjectCode { get; set; }

        /// <summary>Dimensione analitica 2 (in MTF: risorsa).</summary>
        public string? CostingCode2 { get; set; }

        /// <summary>Dimensione analitica 3 (in MTF: business unit).</summary>
        public string? CostingCode3 { get; set; }

        /// <summary>Gruppo IVA. Null ⇒ non inviato: SAP applica il default di articolo/cliente.</summary>
        public string? VatGroup { get; set; }
    }

    public class SalesDocumentAttachment
    {
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public string? ContentBase64 { get; set; }
    }

    /// <summary>Risposta di <c>POST /api/sales-documents</c>.</summary>
    public class SalesDocumentResult
    {
        public bool Success { get; set; }

        /// <summary>Tipo del documento REALMENTE trovato o creato ("invoice"/"order"): il portale lo confronta con quello chiesto.</summary>
        public string? DocumentKind { get; set; }

        /// <summary>"draft" oppure "posted", sempre riferito al documento reale.</summary>
        public string? Status { get; set; }

        /// <summary>Tipo oggetto SAP: "13" fattura, "17" ordine, "112" bozza.</summary>
        public string? ObjectType { get; set; }

        public int? DocEntry { get; set; }
        public int? DocNum { get; set; }
        public decimal? DocTotal { get; set; }
        public decimal? VatSum { get; set; }
        public DateTime? DocDueDate { get; set; }

        /// <summary>Vero se il documento esisteva già per lo stesso correlationId: nessun doppione creato.</summary>
        public bool AlreadyExisted { get; set; }

        public int? AttachmentEntry { get; set; }

        /// <summary>Errore dell'allegato. Non annulla il documento, che resta creato.</summary>
        public string? AttachmentError { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>Avvisi non bloccanti (es. unità di misura non applicabile all'articolo). Campo in più rispetto al contratto minimo.</summary>
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>Anteprima del payload Service Layer, senza scrivere nulla in SAP (collaudo).</summary>
    public class SalesDocumentPreview
    {
        public string? Entity { get; set; }
        public Dictionary<string, object?>? Payload { get; set; }
        public SalesDocumentResult? Existing { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>Documento di vendita già presente in SAP per un correlationId (fattura, ordine o bozza).</summary>
    public sealed class ExistingSalesDocument
    {
        public string Table { get; set; } = string.Empty;
        public string DocumentKind { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int DocEntry { get; set; }
        public int? DocNum { get; set; }
        public decimal? DocTotal { get; set; }
        public decimal? VatSum { get; set; }
        public DateTime? DocDueDate { get; set; }

        /// <summary><c>CANCELED</c> di SAP: 'N' valido, 'Y' annullato, 'C' documento di annullamento.</summary>
        public string Canceled { get; set; } = "N";
    }

    // ── Progetto contabile (OPRJ) ──

    /// <summary>Richiesta di <c>POST /api/fiscal-projects</c>.</summary>
    public class FiscalProjectCreateRequest
    {
        public string? Name { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }

        /// <summary>Codice esplicito. Null ⇒ generato dal pattern <c>SapB1:FiscalProjectCodePattern</c>.</summary>
        public string? Code { get; set; }
    }

    public class FiscalProjectCreateResult
    {
        public string? Code { get; set; }

        /// <summary>False se il codice esplicito esisteva già (nessuna modifica) o se la creazione è fallita.</summary>
        public bool Created { get; set; }

        public string? ErrorMessage { get; set; }
    }

    // ── Ordini cliente correlati al portale ──

    /// <summary>Ordine cliente (ORDR) nato da un documento del portale (<c>U_AX360_InvId</c> valorizzato).</summary>
    public class ErpSalesOrderDto
    {
        public string CorrelationId { get; set; } = string.Empty;
        public int DocEntry { get; set; }
        public int? DocNum { get; set; }
        public string? CardCode { get; set; }
        public DateTime? DocDate { get; set; }

        /// <summary>"open", "closed" oppure "cancelled".</summary>
        public string Status { get; set; } = "open";

        /// <summary>Totale documento IVA inclusa (<c>DocTotal</c>).</summary>
        public decimal DocTotal { get; set; }

        /// <summary>
        /// Residuo da evadere, IMPONIBILE (somma di <c>RDR1.OpenSum</c>, IVA esclusa). Zero se l'ordine non è
        /// aperto: un ordine chiuso o annullato non ha più nulla da fatturare.
        /// </summary>
        public decimal OpenAmount { get; set; }

        /// <summary>Fatture (OINV non annullate) con almeno una riga tratta dall'ordine (<c>INV1.BaseType = 17</c>).</summary>
        public List<ErpSalesOrderInvoiceDto> Invoices { get; set; } = new();
    }

    public class ErpSalesOrderInvoiceDto
    {
        public int DocEntry { get; set; }
        public int? DocNum { get; set; }
        public DateTime? DocDate { get; set; }
    }
}

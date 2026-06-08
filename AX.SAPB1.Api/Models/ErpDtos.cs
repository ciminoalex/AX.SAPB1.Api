namespace AX.SAPB1.Api.Models
{
    // ──────────────────────────────────────────────────────────────────────────
    // DTO del contratto ERP consumato dal portale AX.360 (connettore SapB1ErpConnector).
    // I nomi delle proprietà sono serializzati in camelCase (default ASP.NET Core) e
    // combaciano con i DTO neutri lato host (ErpInvoiceDto, ErpLedgerEntryDto, ...).
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Fattura A/R definitiva (OINV) esposta al mirror finanziario AX.360.</summary>
    public class ErpInvoiceDto
    {
        public string? ErpDocId { get; set; }
        public string ErpDocNumber { get; set; } = string.Empty;
        public string DocType { get; set; } = "altro";

        /// <summary>
        /// Vero se il documento è una nota di credito A/R (ORIN). Per le note di credito tutti gli importi
        /// (TaxableAmount, VatAmount, TotalAmount, PaidAmount e gli importi di righe/rate) sono esposti con
        /// segno NEGATIVO, così il portale li somma come riduzione di fatturato ed esposizione.
        /// </summary>
        public bool IsCreditNote { get; set; }

        public string? ErpCustomerCode { get; set; }

        /// <summary>Codice interno AX.360 (Invoice.Id) letto dall'UDF, se la fattura proviene da una bozza AX.</summary>
        public string? Ax360InvoiceId { get; set; }

        public DateTime? IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Currency { get; set; } = "EUR";

        public decimal TaxableAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }

        public string? Notes { get; set; }

        public List<ErpInvoiceLineDto> Lines { get; set; } = new();
        public List<ErpPaymentInstallmentDto> Installments { get; set; } = new();
    }

    public class ErpInvoiceLineDto
    {
        public int SortOrder { get; set; }
        public string? ErpItemCode { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxableAmount { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class ErpPaymentInstallmentDto
    {
        public int InstallmentNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    /// <summary>Movimento di partitario cliente esposto al portale AX.360.</summary>
    public class ErpLedgerEntryDto
    {
        public string ErpCustomerCode { get; set; } = string.Empty;
        public string ErpDocNumber { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string DocType { get; set; } = "altro";
        public string? Description { get; set; }
        public string Currency { get; set; } = "EUR";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    // ── Push fatture (AX.360 → ERP): creazione bozza ──

    /// <summary>Payload inviato dal portale AX.360 per creare una bozza di fattura A/R in SAP B1.</summary>
    public class ErpInvoicePushDto
    {
        public string ErpCustomerCode { get; set; } = string.Empty;

        /// <summary>Numero leggibile AX.360 (es. "FT-2026-0001").</summary>
        public string DocNumber { get; set; } = string.Empty;

        /// <summary>Codice interno AX.360 (Invoice.Id): chiave di correlazione stabile salvata nell'UDF.</summary>
        public string Ax360InvoiceId { get; set; } = string.Empty;

        public DateTime? IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Currency { get; set; } = "EUR";

        public decimal TaxableAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public string? Notes { get; set; }
        public List<ErpInvoicePushLineDto> Lines { get; set; } = new();
    }

    public class ErpInvoicePushLineDto
    {
        public int SortOrder { get; set; }
        public string? ErpItemCode { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxableAmount { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class ErpInvoicePushResult
    {
        public bool Success { get; set; }
        public string? ErpDocId { get; set; }
        public string? ErpDocNumber { get; set; }

        /// <summary>"draft" quando creata come bozza in attesa di conferma manuale in SAP.</summary>
        public string? DocStatus { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>Documento ERP (bozza o definitivo) già esistente per un dato codice interno AX.360.</summary>
    public sealed class ExistingErpDocument
    {
        public string ErpDocId { get; set; } = string.Empty;
        public string ErpDocNumber { get; set; } = string.Empty;

        /// <summary>"draft" se trovato in ODRF (bozza), "posted" se trovato in OINV (definitiva).</summary>
        public string DocStatus { get; set; } = "draft";
    }
}

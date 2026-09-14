namespace AX.SAPB1.Api.Models
{
    // ─────────────────────────────────────────────────────────────────────────
    // Contratto ERP-NEUTRO per la contabilità generale.
    //
    // Il portale AX.360 non conosce SAP B1: non deve mai vedere OACT, JDT1, OPRJ,
    // GroupMask, TransType o ProfitCode. La traduzione SAP → contratto neutro vive
    // qui e in DbOdbcService.Gl.cs. Se un domani si aggiunge un secondo ERP, è
    // questo il contratto che quel servizio deve implementare.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Sezioni di conto economico, neutre. Mappate da OACT.GroupMask 4..8.</summary>
    public static class GlStatementSection
    {
        public const string ProductionValue = "production_value";        // GM4 — Valore della produzione
        public const string ProductionCost = "production_cost";          // GM5 — Costi della produzione
        public const string OperatingCost = "operating_cost";            // GM6 — Costi operativi
        public const string OtherIncomeExpense = "other_income_expense"; // GM7 — Altri proventi ed oneri
        public const string Extraordinary = "extraordinary";             // GM8 — Proventi ed oneri straordinari
    }

    /// <summary>Tipo del documento che ha originato la scrittura. Neutro, mappato da OJDT.TransType.</summary>
    public static class GlSourceDocType
    {
        public const string SalesInvoice = "fattura_vendita";
        public const string PurchaseInvoice = "fattura_acquisto";
        public const string SalesCreditNote = "nota_credito_attiva";
        public const string PurchaseCreditNote = "nota_credito_passiva";
        public const string ManualJournal = "journal_manuale";
        public const string Payment = "pagamento";
        public const string Other = "altro";
    }

    /// <summary>Conto del piano dei conti. Esposti solo i conti di conto economico.</summary>
    public class GlAccountDto
    {
        public string ErpAccountCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ParentCode { get; set; }
        public bool IsPostable { get; set; }
        public bool IsIncomeStatement { get; set; }

        /// <summary>
        /// Sezione di bilancio secondo la classificazione dell'ERP (vedi <see cref="GlStatementSection"/>).
        /// È informativa, per il raggruppamento di display: NON è la natura economica, che è una decisione
        /// umana e vive nel portale. In MTF il costo del lavoro e gli ammortamenti stanno entrambi in
        /// operating_cost insieme agli affitti: la sezione non discrimina la natura.
        /// </summary>
        public string? StatementSection { get; set; }
    }

    /// <summary>Periodo contabile. È la sola fonte della nozione di anno fiscale e di periodo aperto.</summary>
    public class GlFiscalPeriodDto
    {
        public string Code { get; set; } = string.Empty;   // es. "2026-07"
        public int FiscalYear { get; set; }
        public int PeriodNumber { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsOpen { get; set; }
    }

    /// <summary>
    /// Progetto finanziario/contabile dell'ERP: il codice che si scrive sulla riga contabile.
    /// Distinto dal progetto del modulo di project management, che ha un namespace suo.
    /// </summary>
    public class GlFiscalProjectDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>Dimensione analitica dell'ERP (1..5). In MTF: 1=Area, 2=Risorsa, 3=Business Unit.</summary>
    public class GlDimensionDto
    {
        public int Number { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>Centro di costo / regola di distribuzione, valorizzabile su una dimensione.</summary>
    public class GlDistributionRuleDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int DimensionNumber { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Riga di contabilità generale. È lo specchio della verità ERP: <see cref="ErpProjectCode"/> e le
    /// dimensioni sono l'attribuzione autorevole, non una proposta del portale.
    /// </summary>
    public class GlLineDto
    {
        /// <summary>Id della registrazione (OJDT.TransId).</summary>
        public int ErpEntryId { get; set; }

        /// <summary>Id della riga dentro la registrazione (JDT1.Line_ID). Insieme a ErpEntryId è la PK.</summary>
        public int ErpLineId { get; set; }

        public string ErpAccountCode { get; set; } = string.Empty;
        public string? ErpAccountName { get; set; }
        public string? StatementSection { get; set; }

        public DateTime PostingDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int FiscalYear { get; set; }

        public string SourceDocType { get; set; } = GlSourceDocType.Other;
        public string? SourceDocNumber { get; set; }

        /// <summary>Controparte del documento d'origine, quando esiste. Null per i journal manuali.</summary>
        public string? ErpCounterpartyCode { get; set; }
        public string? ErpCounterpartyName { get; set; }

        /// <summary>"customer" | "supplier" | null.</summary>
        public string? ErpCounterpartyType { get; set; }

        public string? ErpProjectCode { get; set; }

        /// <summary>
        /// Codice progetto dichiarato sulla testata del documento d'origine (fattura o nota di credito),
        /// quando esiste. Non è l'attribuzione della riga: serve solo come <b>ripiego</b> a chi legge, per le
        /// righe che non portano un <see cref="ErpProjectCode"/> proprio. Null per i documenti senza testata
        /// (journal manuali, pagamenti) o con testata vuota. Mai riscritto verso l'ERP.
        /// </summary>
        public string? HeaderProjectCode { get; set; }

        /// <summary>
        /// Codice progetto <b>contabile</b> del lavoro fatturato: ricavato dai timesheet collegati alle righe
        /// del documento d'origine (fatture e note di credito di vendita), solo quando tutte le righe di quel
        /// conto portano un timesheet con lo stesso codice. Più preciso della testata, che sulle fatture T&amp;M
        /// a volte indica un'altra commessa. Come <see cref="HeaderProjectCode"/> è un ripiego per chi legge:
        /// non è l'attribuzione della riga e non si riscrive mai verso l'ERP.
        /// </summary>
        public string? WorkProjectCode { get; set; }

        public string? Dimension1Code { get; set; }
        public string? Dimension2Code { get; set; }
        public string? Dimension3Code { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string Currency { get; set; } = "EUR";
        public string? Memo { get; set; }
    }

    /// <summary>
    /// I quattro campi analitici scrivibili su una riga contabile. Stringa vuota = campo azzerato.
    /// Non nullable: chi scrive dichiara sempre lo stato completo desiderato, così l'UPDATE ha una
    /// SET list costante e un ordine dei parametri immune agli errori posizionali di ODBC.
    /// </summary>
    public class GlLineAttributionValues
    {
        public string ProjectCode { get; set; } = string.Empty;
        public string Dimension1Code { get; set; } = string.Empty;
        public string Dimension2Code { get; set; } = string.Empty;
        public string Dimension3Code { get; set; } = string.Empty;
    }

    /// <summary>Una riga da aggiornare, con lo stato desiderato e quello atteso (compare-and-swap).</summary>
    public class GlLineAttributionItem
    {
        public int ErpEntryId { get; set; }
        public int ErpLineId { get; set; }

        public GlLineAttributionValues Desired { get; set; } = new();

        /// <summary>
        /// Stato che il chiamante ha letto e su cui ha deciso. L'UPDATE lo mette in WHERE: se nel frattempo
        /// qualcuno ha modificato la riga dal client SAP (che non prende lock sui form), l'UPDATE non tocca
        /// nulla e la riga torna in conflitto invece di sovrascrivere in silenzio.
        /// </summary>
        public GlLineAttributionValues Expected { get; set; } = new();
    }

    public class GlLineAttributionItemResult
    {
        public int ErpEntryId { get; set; }
        public int ErpLineId { get; set; }
        public bool Updated { get; set; }

        /// <summary>true quando la riga è cambiata sotto: rileggere e riproporre all'operatore.</summary>
        public bool Conflict { get; set; }

        public bool NotFound { get; set; }

        /// <summary>Valori RILETTI dal DB dopo l'update, mai l'eco dell'input.</summary>
        public GlLineAttributionValues? Current { get; set; }
    }

    public class GlAttributionRequest
    {
        public List<GlLineAttributionItem> Items { get; set; } = new();
    }

    public class GlAttributionResult
    {
        public bool Success { get; set; }
        public int Updated { get; set; }
        public int Conflicts { get; set; }
        public List<GlLineAttributionItemResult> Items { get; set; } = new();

        /// <summary>Codici progetto/centro di costo inesistenti: la richiesta è respinta INTERA, nulla è scritto.</summary>
        public List<string> InvalidCodes { get; set; } = new();

        public string? ErrorMessage { get; set; }
    }
}

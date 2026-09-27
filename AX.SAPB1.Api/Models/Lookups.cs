namespace AX.SAPB1.Api.Models
{
    public class CustomerSummary
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        // Anagrafica estesa consumata dal portale AX (ErpCustomerDto): mappata da OCRD.
        public string? VatNumber { get; set; }
        public string? TaxCode { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
    }

    public class ContactSummary
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Profilo anagrafico cliente esteso (ERP-neutro) consumato dal portale AX (ExternalCustomerProfile).
    /// Mappato da OCRD + join (OCTG termini di pagamento, OPYM modalità, OSLP agente, OCRG gruppo BP).
    /// Tutti i campi oltre a CardCode sono best-effort: null se non valorizzati in SAP.
    /// </summary>
    public class CustomerProfile
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Iban { get; set; }
        public string? PaymentTermsLabel { get; set; }
        public string? PaymentMethod { get; set; }
        public string? SalesAgent { get; set; }
        public string? BusinessPartnerGroup { get; set; }
        public DateTime? CustomerSince { get; set; }
        public decimal? CreditLimit { get; set; }
        public decimal? CurrentBalance { get; set; }
    }

    public class ProjectSummary
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        // Dati cliente del progetto, consumati dal portale AX (ErpProjectDto): da OPMG.CARDCODE → OCRD.
        public string? CardCode { get; set; }
        public string? CardName { get; set; }

        /// <summary>
        /// Codice del progetto CONTABILE associato a questo progetto di project management.
        /// Namespace disgiunto da <see cref="Code"/>: qui codici come "PRJ26_2600016", là chiavi
        /// surrogate numeriche. null quando il progetto non ha ancora un contabile associato.
        /// </summary>
        public string? FiscalProjectCode { get; set; }
    }

    public class ProjectLookupDetail
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
    }

    public class ActivitySummary
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string UoM { get; set; } = string.Empty;
        public decimal Price { get; set; } = 0;
        public decimal UoMPrice { get { 
                switch (UoM) 
                { 
                    case "GG": return Price / 8;
                    case "HH": return Price;
                    default: return 0;
                }
            } 
        }
    }

    public class ResourceSummary
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Articolo di magazzino/servizio (OITM) per la scelta dell'articolo sulle righe dei documenti di vendita
    /// del portale. <see cref="Active"/> è calcolato su validFor/frozenFor con le loro date (vedi
    /// <c>DbOdbcService.IsItemActive</c>).
    /// </summary>
    public class ErpItemDto
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? GroupName { get; set; }
        public bool Active { get; set; }

        /// <summary>Gruppo IVA di vendita di default dell'articolo (<c>OITM."VatGourpSa"</c>, refuso SAP).</summary>
        public string? SalesVatGroup { get; set; }
    }

    public class ActivityTimeTotal
    {
        public string Project { get; set; } = string.Empty;
        public string ActivityId { get; set; } = string.Empty;
        public decimal TimeTot { get; set; }
    }
}



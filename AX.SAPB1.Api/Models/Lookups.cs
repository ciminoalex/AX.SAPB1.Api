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

    public class ProjectSummary
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        // Dati cliente del progetto, consumati dal portale AX (ErpProjectDto): da OPMG.CARDCODE → OCRD.
        public string? CardCode { get; set; }
        public string? CardName { get; set; }
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

    public class ActivityTimeTotal
    {
        public string Project { get; set; } = string.Empty;
        public string ActivityId { get; set; } = string.Empty;
        public decimal TimeTot { get; set; }
    }
}



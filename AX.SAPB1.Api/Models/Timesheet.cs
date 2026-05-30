using System.ComponentModel.DataAnnotations;

namespace AX.SAPB1.Api.Models
{
    public class Timesheet
    {
        // Campi di sistema SAP B1
        public int? DocEntry { get; set; }
        public string? Code { get; set; } // Campo standard alfanumerico (20)
        
        // Campi di identificazione risorsa
        [Required]
        public string? ResId { get; set; } // Id Risorsa (Alfanumerico 50)
        [Required]
        public string? CardCode { get; set; } // Codice BP (Alfanumerico 100)
        public string? CardName { get; set; } // Ragione Sociale (Alfanumerico 100)
        public string? RefId { get; set; } // Id Referente (Alfanumerico 10)
        public string? RefData { get; set; } // Dati Referente (Alfanumerico 250)
        
        // Campi di progetto e attività
        [Required]
        public string? Project { get; set; } // Progetto (Alfanumerico 100)
        public string? ProjectName { get; set; } // Nome Progetto (Alfanumerico 200)
        public string? SubProject { get; set; } // Sottoprogetto (Alfanumerico 100)
        public string? Activity { get; set; } // Attività (Alfanumerico 100)
        [Required]
        public string? ActivityId { get; set; } // Attività (Alfanumerico 100)
        public string? SubActivity { get; set; } // Attività Sottoprogetto (Alfanumerico 100)
        public string? ActivityName { get; set; } // Nome Attività (Alfanumerico 200)
        
        // Campi temporali principali
        public DateTime Date { get; set; } // Data Attività
        public int? TimeStart { get; set; } // Ora Inizio
        public int? TimeEnd { get; set; } // Ora Fine
        public int? TimePa { get; set; } // Ore Numero Pausa
        public int? TimeNF { get; set; } // Ore Numero Non Fatturabili
        
        // Campi ore numeriche
        public decimal? TimeNrPa { get; set; } // Ore Numero Totali
        public decimal? TimeNrNF { get; set; } // Ore Numero Netto
        public decimal? TimeNrTot { get; set; } // Ore Numero Totali
        public decimal? TimeNrNet { get; set; } // Ore Numero Netto
       
        // Campi descrittivi
        public string? DescExt { get; set; } // Descrizione Esterna
        public string? DescInt { get; set; } // Descrizione Interna
        
        // Campi articolo e stato
        public string? Status { get; set; } // Stato (Alfanumerico 100)
        
    }

    public class TimesheetCreateRequest
    {
        [Required]
        public DateTime Date { get; set; }
        
        [Required]
        public string ResId { get; set; } = string.Empty;
        
        [Required]
        public string CardCode { get; set; } = string.Empty;
        
        public string? CardName { get; set; }
        
        public string? RefId { get; set; }
        
        public string? RefData { get; set; }
        
        [Required]
        public string Project { get; set; } = string.Empty;
        
        public string? ProjectName { get; set; }
        
        public string? SubProject { get; set; }
        
        public string? Activity { get; set; }
        
        [Required]
        public string ActivityId { get; set; } = string.Empty;
        
        public string? SubActivity { get; set; }
        
        public string? ActivityName { get; set; }
        
        public int? TimeStart { get; set; }
        
        public int? TimeEnd { get; set; }
        
        public int? TimePa { get; set; }
        
        public int? TimeNF { get; set; }
        
        public decimal? TimeNrPa { get; set; }
        
        public decimal? TimeNrNF { get; set; }
        
        public decimal? TimeNrTot { get; set; }
        
        public decimal? TimeNrNet { get; set; }
        
        public string? DescExt { get; set; }
        
        public string? DescInt { get; set; }
        
        public string? Status { get; set; }
    }

    public class TimesheetCreateRequestLite
    {
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public string ResId { get; set; } = string.Empty;
        [Required]
        public string Project { get; set; } = string.Empty;
        [Required]
        public string ActivityId { get; set; } = string.Empty;
        [Required]
        public decimal? Hours { get; set; }
        [Required]
        public string? Desc { get; set; }
    }

    public class TimesheetServiceLayerPayload
    {
        public string U_ResId { get; set; } = string.Empty;
        public string U_Date { get; set; } = string.Empty;
        public string U_CardCode { get; set; } = string.Empty;
        public string U_CardName { get; set; } = string.Empty;
        public string U_Project { get; set; } = string.Empty;
        public string U_ProjectName { get; set; } = string.Empty;
        public string U_Activity { get; set; } = string.Empty;
        public string U_ActivityName { get; set; } = string.Empty;
        public string U_TimeStart { get; set; } = string.Empty;
        public string U_TimeEnd { get; set; } = string.Empty;
        public string U_TimePa { get; set; } = string.Empty;
        public decimal U_TimeNrPa { get; set; }
        public decimal U_TimeNrNF { get; set; }
        public decimal U_TimeNrTot { get; set; }
        public decimal U_TimeNrNet { get; set; }
        public string? U_DescExt { get; set; }
        public string U_Status { get; set; } = string.Empty;
        public string U_ActivityId { get; set; } = string.Empty;
        public decimal U_TimeNrPaOri { get; set; }
        public decimal U_TimeNrNFOri { get; set; }
        public decimal U_TimeNrTotOri { get; set; }
        public decimal U_TimeNrNetOri { get; set; }
    }

    public class TimesheetUpdateRequest
    {
        [Required]
        public int DocEntry { get; set; }
        
        public DateTime? Date { get; set; }
        
        public string? ResId { get; set; }
        
        public string? CardCode { get; set; }
        
        public string? CardName { get; set; }
        
        public string? RefId { get; set; }
        
        public string? RefData { get; set; }
        
        public string? Project { get; set; }
        
        public string? ProjectName { get; set; }
        
        public string? SubProject { get; set; }
        
        public string? Activity { get; set; }
        
        public string? ActivityId { get; set; }
        
        public string? SubActivity { get; set; }
        
        public string? ActivityName { get; set; }
        
        public int? TimeStart { get; set; }
        
        public int? TimeEnd { get; set; }
        
        public int? TimePa { get; set; }
        
        public int? TimeNF { get; set; }
        
        public decimal? TimeNrPa { get; set; }
        
        public decimal? TimeNrNF { get; set; }
        
        public decimal? TimeNrTot { get; set; }
        
        public decimal? TimeNrNet { get; set; }
        
        public string? DescExt { get; set; }
        
        public string? DescInt { get; set; }
        
        public string? Status { get; set; }
    }
}

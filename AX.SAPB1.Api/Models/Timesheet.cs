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

        /// <summary>
        /// Colonna di sistema <c>Canceled</c> della riga (JSON <c>canceled</c>): <c>true</c> = riga annullata in SAP
        /// (<c>'Y'</c>), <c>false</c> = attiva (<c>'N'</c>), <c>null</c> = non letta o illeggibile. Campo AGGIUNTO in
        /// coda al contratto (additivo: chi non lo legge non cambia).
        /// <para>
        /// Oggi lo valorizza solo <c>GET /api/timesheet/employee/{employeeId}/daterange</c>, la ricerca che il portale
        /// usa per ritrovare una riga dopo un push andato in timeout: quella query restituisce ANCHE le righe annullate
        /// (serve ad altri chiamanti, non si filtra), e senza questo campo una riga annullata con gli stessi attributi
        /// sembrerebbe la riga creata dal push. Le altre letture di timesheet lo lasciano a <c>null</c>: lì vuol dire
        /// "non letto", non "attiva".
        /// </para>
        /// </summary>
        public bool? Canceled { get; set; }
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

        /// <summary>
        /// Ore fatturabili della riga (facoltativo). SGS fattura il T&amp;M da <c>U_TimeNrNet</c>, non dalle ore
        /// lorde: se il capo progetto riduce nel portale le ore da fatturare, il valore ridotto deve finire lì,
        /// altrimenti SGS fattura comunque le ore piene. Assente o <c>null</c> = uguale a <see cref="Hours"/>
        /// (comportamento di prima, i chiamanti che non lo mandano non cambiano). Vincolo
        /// <c>0 &lt;= BillableHours &lt;= Hours</c>, altrimenti 400. Scritto come <c>U_TimeNrTot = Hours</c>,
        /// <c>U_TimeNrNet = BillableHours</c>, <c>U_TimeNrNF = Hours - BillableHours</c>.
        /// </summary>
        public decimal? BillableHours { get; set; }
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

    /// <summary>
    /// Stato di fatturazione di una riga di timesheet, contratto ERP-neutro (nessun nome SAP nel portale).
    /// Sola lettura: <see cref="State"/> è la traduzione di <c>U_Status</c> (vedi
    /// <see cref="Services.DbOdbcService.MapBillingState"/>), <see cref="InvoiceErpDocNumber"/> e
    /// <see cref="InvoicedOn"/> la fattura che porta la riga quando <c>U_DestType = '13'</c>.
    /// <para>
    /// <see cref="ErpDocId"/> è <c>@SGS_PRJ_OTMS.DocEntry</c>, <b>non</b> <c>Code</c>: è l'identificativo che
    /// il servizio restituisce al portale quando la riga viene creata (<c>POST</c> di timesheet) e che il
    /// portale conserva. Le due colonne non coincidono — misurato il 16/09/2026 su 7.019 righe, 826 (11,8%)
    /// hanno <c>Code</c> diverso da <c>DocEntry</c>, e sulle righe recenti lo scarto è costante (la 7128 porta
    /// <c>Code</c> «7121») — quindi rispondere con <c>Code</c> farebbe agganciare al portale lo stato di
    /// un'altra riga di timesheet: ore marcate come fatturate per una fattura che non le riguarda.
    /// </para>
    /// <para>
    /// <see cref="ErpResourceCode"/>, <see cref="ErpProjectCode"/>, <see cref="ErpActivityCode"/> e
    /// <see cref="WorkedOn"/> non sono decorazione: sono la <b>chiave di riserva</b> per le righe di
    /// timesheet del portale che non hanno mai ricevuto <see cref="ErpDocId"/> — misurate in produzione il
    /// 16/09/2026, 414 righe (120 mai spinte a SAP, 293 marcate "da non esportare" perché inserite a mano
    /// direttamente in SAP). Per queste il portale non ha alcun identificativo da correlare e deve abbinare
    /// per attributi (risorsa + progetto + attività + data). Direzione decisa dal titolare: si legge da SAP
    /// e si aggiorna il portale, non il contrario — questo endpoint resta sola lettura.
    /// </para>
    /// </summary>
    public class TimesheetBillingState
    {
        public string ErpDocId { get; set; } = string.Empty;   // @SGS_PRJ_OTMS.DocEntry (MAI Code)
        public string State { get; set; } = string.Empty;      // "invoiced" | "confirmed" | "draft"
        public string? InvoiceErpDocNumber { get; set; }       // OINV.DocNum
        public DateTime? InvoicedOn { get; set; }               // OINV.DocDate
        public decimal Hours { get; set; }                     // U_TimeNrNet (ore fatturabili: quelle che SGS fattura)

        // U_TimeNrTot, le ore lorde della riga. Campo AGGIUNTO in coda al contratto (additivo: chi non lo
        // legge non cambia): serve al portale per il merge a tre vie delle ore già spinte, dove "loro" è
        // la coppia (lorde, fatturabili) come la vede SAP. A differenza di Hours (contratto esistente, che
        // resta a zero) un valore assente o illeggibile vale null: il portale tratta null come "il servizio
        // non lo sa" e non confronta, mentre uno zero inventato sembrerebbe uno scostamento di ore.
        public decimal? TotalHours { get; set; }

        // Chiave di riserva per l'abbinamento per attributi (vedi doc di classe): U_ResId, il codice
        // persona SAP (es. "Dip_41_MioNoe").
        public string? ErpResourceCode { get; set; }

        // U_Project: identificativo di progetto SAP, numerico ma trasportato come stringa (stesso motivo
        // di ErpDocId — il contratto verso il portale è ERP-neutro).
        public string? ErpProjectCode { get; set; }

        // U_Activity, trasportato COSÌ COM'È, senza normalizzare lo zero-padding: su SAP arriva "7", sul
        // portale la stessa attività è salvata "07". Il confronto tollerante alla differenza è compito del
        // portale, non di questo servizio.
        public string? ErpActivityCode { get; set; }

        // U_Date: la data della rendicontazione in SAP, per l'abbinamento per attributi.
        public DateTime? WorkedOn { get; set; }
    }
}

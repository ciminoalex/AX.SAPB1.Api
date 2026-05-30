namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Nomi dei campi utente (UDF) creati dal servizio sui documenti di marketing SAP B1 per
    /// correlare le fatture del portale AX.360 alle bozze/fatture definitive. I campi sono
    /// condivisi tra bozza (ODRF) e fattura (OINV): il valore impostato sulla bozza si propaga
    /// alla fattura definitiva quando l'operatore la conferma in SAP.
    ///
    /// In SAP B1 il nome logico (senza prefisso) viene salvato come colonna con prefisso "U_".
    /// </summary>
    public static class Ax360Udf
    {
        /// <summary>Chiave di correlazione: codice interno AX.360 della fattura (GUID Invoice.Id).</summary>
        public const string InvId = "AX360_InvId";

        /// <summary>Numero leggibile AX.360 della fattura (es. "FT-2026-0001").</summary>
        public const string InvNum = "AX360_InvNum";

        /// <summary>Tipo documento per il mirror finanziario AX (canone|manutenzione|servizio|altro).</summary>
        public const string DocType = "AX360_DocType";

        /// <summary>Nome colonna fisica (prefisso "U_") del campo utente.</summary>
        public static string Col(string name) => "U_" + name;
    }
}

namespace AX.SAPB1.Api.Services.SalesDocuments
{
    /// <summary>Valori ERP-neutri del contratto dei documenti di vendita (tipo, stato, oggetto SAP).</summary>
    public static class SalesDocumentKinds
    {
        public const string Invoice = "invoice";
        public const string Order = "order";

        public const string Draft = "draft";
        public const string Posted = "posted";

        /// <summary>Tipi oggetto SAP B1 restituiti al portale.</summary>
        public const string ObjectTypeInvoice = "13";
        public const string ObjectTypeOrder = "17";
        public const string ObjectTypeDraft = "112";
    }

    /// <summary>
    /// Dove finisce un documento: entità del Service Layer, tabelle SAP della destinazione e tabella di
    /// testata "del tipo" (OINV/ORDR), che è quella in cui il documento vivrà da definitivo — anche quando
    /// oggi è una bozza in ODRF.
    /// </summary>
    public sealed record SalesDocumentTarget(
        string DocumentKind,
        string Posting,
        string Entity,
        string? DocObjectCode,
        string HeaderTable,
        string LineTable,
        string ObjectType,
        string KindHeaderTable)
    {
        public bool IsDraft => Posting == SalesDocumentKinds.Draft;
    }

    /// <summary>Unità di misura risolta per una riga: <c>UoMEntry</c> per gli articoli gestiti a gruppi, <c>MeasureUnit</c> (testo) per quelli manuali.</summary>
    public readonly record struct LineUom(int? UoMEntry, string? MeasureUnit);

    /// <summary>Gruppo unità di misura di un articolo (<c>OITM.UgpEntry</c>, -1 = manuale) e le unità che contiene (<c>UGP1</c>).</summary>
    public sealed record ItemUomInfo(int UgpEntry, IReadOnlySet<int> GroupUomEntries);

    /// <summary>Unità di misura lette da SAP per le righe di un documento: codice → <c>UomEntry</c>, articolo → gruppo UdM.</summary>
    public sealed class UomResolution
    {
        public Dictionary<string, int> UomEntryByCode { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ItemUomInfo> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Metadati dello schema SAP che condizionano il payload: campi utente presenti sulla testata di
    /// destinazione e lunghezze delle colonne (nome colonna → lunghezza massima in caratteri).
    /// </summary>
    public sealed class SalesDocumentSchema
    {
        public IReadOnlySet<string> HeaderUserFields { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, int> HeaderLengths { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, int> LineLengths { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Riga di <c>ATC1</c> (allegati della company): voce di Attachments2, nome del file senza estensione, estensione.</summary>
    public sealed record AttachmentFileRow(int AbsEntry, string FileName, string? FileExt);

    /// <summary>File da caricare in <c>Attachments2</c>.</summary>
    public sealed record ServiceLayerFile(string FileName, string ContentType, byte[] Content);

    /// <summary>Esito grezzo di una chiamata al Service Layer: il chiamante decide cosa è un errore.</summary>
    public sealed record ServiceLayerResponse(int StatusCode, bool IsSuccess, string Body);
}

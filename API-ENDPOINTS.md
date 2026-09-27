# AX SAP B1 API — Mappa endpoint e modelli

Documentazione per integrazione client (es. interfaccia web). Generata dal codice sorgente ASP.NET Core.

## Base URL pubblica

**Indirizzo attuale delle API:** `https://timesheet.mtfapps.it/api/`

Tutti i path sotto sono relativi a questa base (es. login completo: `POST https://timesheet.mtfapps.it/api/auth/login`).

---

## Convenzioni generali

| Aspetto | Dettaglio |
|--------|-----------|
| Formato | JSON (`Content-Type: application/json` dove applicabile) |
| Date/ora | Query `startDate` / `endDate` e campi body: tipicamente ISO 8601 (es. `2025-03-25` o `2025-03-25T10:00:00Z`) |
| Autenticazione | JWT Bearer (vedi sotto) |
| CORS | Policy permissiva (`AllowAnyOrigin`, header e metodi) |

### Autenticazione JWT

1. `POST /api/auth/login` con credenziali SAP B1 (vedi sotto).
2. Risposta: `{ "token": "<jwt>", "expiresIn": <secondi> }`.
3. Richieste successive: header `Authorization: Bearer <token>`.

Gli endpoint marcati **Protetto** richiedono questo header. Il token è legato alle credenziali salvate lato server per le operazioni SAP.

---

## Riepilogo endpoint

| Metodo | Path | Protetto | Descrizione |
|--------|------|----------|-------------|
| POST | `/api/auth/login` | No | Login; validazione credenziali SAP B1; emissione JWT |
| GET | `/api/timesheet` | Sì | Elenco di tutti i timesheet (ODBC) |
| GET | `/api/timesheet/{docEntry}` | Sì | Singolo timesheet per `DocEntry` (int) |
| GET | `/api/timesheet/employee/{employeeId}` | Sì | Timesheet per dipendente |
| GET | `/api/timesheet/employee/{employeeId}/daterange` | Sì | Timesheet per dipendente in intervallo date (query) |
| GET | `/api/timesheet/project/{projectId}` | Sì | Timesheet per progetto |
| GET | `/api/timesheet/daterange` | Sì | Timesheet per intervallo date globale (query) |
| GET | `/api/timesheet/activity-time-tot` | Sì | Totale ore per progetto + attività (query) |
| POST | `/api/timesheet` | Sì | Crea timesheet (SAP Service Layer) |
| PUT | `/api/timesheet/{docEntry}` | Sì | Aggiorna timesheet (SAP Service Layer); `DocEntry` URL = body |
| DELETE | `/api/timesheet/{code}` | Sì | Elimina timesheet per **codice** alfanumerico (non `DocEntry`) |
| GET | `/api/lookup/customers` | Sì | Elenco clienti |
| GET | `/api/customers` | Sì | Profili anagrafici estesi (bulk) per il mirror ExternalCustomerProfile del portale |
| GET | `/api/customers/{cardCode}` | Sì | Profilo anagrafico esteso di un singolo cliente |
| GET | `/api/lookup/customers/{cardCode}/contacts` | Sì | Referenti per cliente |
| GET | `/api/lookup/customers/{cardCode}/projects` | Sì | Progetti per cliente |
| GET | `/api/lookup/projects` | Sì | Elenco progetti |
| GET | `/api/lookup/projects/{projectCode}/activities` | Sì | Attività per progetto |
| GET | `/api/lookup/resources` | Sì | Risorse (dipendenti attivi) |
| POST | `/api/test` | No | Echo/test POST e CORS |
| OPTIONS | `/api/test` | No | Test preflight OPTIONS |

**Swagger UI** (se esposto sullo stesso host): tipicamente `/swagger` — utile per provare le API in ambiente dove è abilitato.

---

## Dettaglio per controller

### Auth (`/api/auth`)

#### `POST /login`

- **Auth:** anonimo.
- **Body:** `LoginRequest`
- **200:** `{ "token": string, "expiresIn": number }` (`expiresIn` in secondi).
- **400:** `UserName` o `Password` mancanti.
- **401:** credenziali non valide per SAP Business One.

---

### Timesheet (`/api/timesheet`)

Tutti richiedono **JWT** salvo diversa indicazione.

#### `GET /`

Elenco completo timesheet dal database ODBC.

- **200:** array di `Timesheet`
- **500:** errore server

#### `GET /{docEntry}`

- **Parametro path:** `docEntry` (int)
- **200:** `Timesheet`
- **404:** non trovato
- **500:** errore server

#### `GET /employee/{employeeId}`

Timesheet per risorsa/dipendente.

- **200:** `Timesheet[]`
- **500:** errore server

#### `GET /employee/{employeeId}/daterange?startDate=&endDate=`

- **Query obbligatorie:** `startDate`, `endDate` (`DateTime`)
- **200:** `Timesheet[]`
- **500:** errore server

#### `GET /project/{projectId}`

- **200:** `Timesheet[]`
- **500:** errore server

#### `GET /daterange?startDate=&endDate=`

Intervallo date senza filtro dipendente.

- **Query:** `startDate`, `endDate`
- **200:** `Timesheet[]`
- **500:** errore server

#### `GET /activity-time-tot?projectId=&activityId=`

Totale ore aggregate per progetto e attività.

- **Query:** `projectId`, `activityId` (stringhe)
- **200:** `ActivityTimeTotal`
- **404:** nessun dato / non trovato
- **500:** errore server

#### `POST /`

Crea documento tramite SAP B1 Service Layer.

- **Body:** `TimesheetCreateRequest`
- **201:** `Timesheet` (header `Location` verso `GET .../timesheet/{docEntry}`)
- **400:** validazione fallita
- **500:** errore server

#### `PUT /{docEntry}`

- **Path:** `docEntry` (int) deve coincidere con `TimesheetUpdateRequest.DocEntry`
- **Body:** `TimesheetUpdateRequest`
- **200:** `Timesheet`
- **400:** modello non valido o `DocEntry` non allineato
- **500:** errore server

#### `DELETE /{code}`

Eliminazione per **codice** timesheet (stringa SAP), non per `DocEntry`.

- **204:** eliminato
- **404:** non trovato o non eliminabile
- **500:** errore server

---

### Lookup (`/api/lookup`)

Tutti richiedono **JWT**.

| Metodo | Path | Risposta 200 |
|--------|------|----------------|
| GET | `/customers` | `CustomerSummary[]` |
| GET | `/customers/{cardCode}/contacts` | `ContactSummary[]` |
| GET | `/customers/{cardCode}/projects` | `ProjectSummary[]` |
| GET | `/projects` | `ProjectSummary[]` |
| GET | `/projects/{projectCode}/activities` | `ActivitySummary[]` |
| GET | `/resources` | `ResourceSummary[]` |

In caso di errore ODBC/query: **500** con messaggio testuale italiano.

---

### Test (`/api/test`)

#### `POST /`

Senza autenticazione. Echo per test CORS/POST.

- **Body:** qualsiasi JSON (opzionale) — `object`
- **200:** oggetto con `status`, `timestampUtc`, `method`, `path`, `headers`, `payload`

#### `OPTIONS /`

Risposta **200** vuota (verifica preflight).

---

## Modelli (DTO / entità)

### `LoginRequest` (Auth)

| Campo | Tipo | Note |
|-------|------|------|
| UserName | string | obbligatorio |
| Password | string | obbligatorio |

### Risposta login (anonimo tipo)

| Campo | Tipo |
|-------|------|
| token | string (JWT) |
| expiresIn | number (secondi) |

---

### `Timesheet`

Entità letta dal DB / restituita dalle API.

| Campo | Tipo | Note |
|-------|------|------|
| DocEntry | int? | Chiave documento SAP |
| Code | string? | Codice alfanumerico (~20) |
| ResId | string? | Id risorsa (required in creazione) |
| CardCode | string? | Codice business partner |
| CardName | string? | Ragione sociale |
| RefId | string? | Id referente |
| RefData | string? | Dati referente |
| Project | string? | Codice progetto |
| ProjectName | string? | |
| SubProject | string? | |
| Activity | string? | |
| ActivityId | string? | |
| SubActivity | string? | |
| ActivityName | string? | |
| Date | DateTime | Data attività |
| TimeStart | int? | Ora inizio |
| TimeEnd | int? | Ora fine |
| TimePa | int? | Pausa |
| TimeNF | int? | Non fatturabili |
| TimeNrPa | decimal? | |
| TimeNrNF | decimal? | |
| TimeNrTot | decimal? | |
| TimeNrNet | decimal? | |
| DescExt | string? | Descrizione esterna |
| DescInt | string? | Descrizione interna |
| Status | string? | |

---

### `TimesheetCreateRequest`

Campi con `[Required]` nel codice: `Date`, `ResId`, `CardCode`, `Project`, `ActivityId`. Gli altri sono opzionali.

| Campo | Tipo | Obbligatorio |
|-------|------|--------------|
| Date | DateTime | sì |
| ResId | string | sì |
| CardCode | string | sì |
| CardName | string? | |
| RefId | string? | |
| RefData | string? | |
| Project | string | sì |
| ProjectName | string? | |
| SubProject | string? | |
| Activity | string? | |
| ActivityId | string | sì |
| SubActivity | string? | |
| ActivityName | string? | |
| TimeStart | int? | |
| TimeEnd | int? | |
| TimePa | int? | |
| TimeNF | int? | |
| TimeNrPa | decimal? | |
| TimeNrNF | decimal? | |
| TimeNrTot | decimal? | |
| TimeNrNet | decimal? | |
| DescExt | string? | |
| DescInt | string? | |
| Status | string? | |

---

### `TimesheetUpdateRequest`

| Campo | Tipo | Note |
|-------|------|------|
| DocEntry | int | obbligatorio; deve combaciare con URL |
| Date | DateTime? | |
| ResId | string? | |
| CardCode | string? | |
| CardName | string? | |
| RefId | string? | |
| RefData | string? | |
| Project | string? | |
| ProjectName | string? | |
| SubProject | string? | |
| Activity | string? | |
| ActivityId | string? | |
| SubActivity | string? | |
| ActivityName | string? | |
| TimeStart | int? | |
| TimeEnd | int? | |
| TimePa | int? | |
| TimeNF | int? | |
| TimeNrPa | decimal? | |
| TimeNrNF | decimal? | |
| TimeNrTot | decimal? | |
| TimeNrNet | decimal? | |
| DescExt | string? | |
| DescInt | string? | |
| Status | string? | |

---

### `ActivityTimeTotal`

| Campo | Tipo |
|-------|------|
| Project | string |
| ActivityId | string |
| TimeTot | decimal |

---

### `CustomerSummary`

| Campo | Tipo |
|-------|------|
| CardCode | string |
| CardName | string |

---

### `ContactSummary`

| Campo | Tipo |
|-------|------|
| Code | string |
| Name | string |

---

### `ProjectSummary`

| Campo | Tipo |
|-------|------|
| Code | string |
| Name | string |

---

### `ActivitySummary`

| Campo | Tipo |
|-------|------|
| Code | string |
| Name | string |
| UoM | string | es. `GG`, `HH` |
| Price | decimal | |
| UoMPrice | decimal | calcolato lato server da `UoM` e `Price` (es. GG → Price/8) |

---

### `ResourceSummary`

| Campo | Tipo |
|-------|------|
| Code | string |
| Name | string |

---

## Note per il client frontend

1. **Ordine di bootstrap:** login → salvare `token` → inviare `Authorization: Bearer …` su lookup e timesheet.
2. **DELETE timesheet:** il path usa **`code`** (stringa), non `DocEntry`.
3. **PUT timesheet:** coerenza obbligatoria tra `docEntry` nell’URL e nel body.
4. **Errori:** molti endpoint restituiscono **500** con corpo stringa in italiano; **400** può essere stringa o `ModelState` JSON.

---

## Integrazione ERP per il portale AX.360

Endpoint consumati dal connettore `SapB1ErpConnector` di AX.360. Autenticazione **machine-to-machine** via header **`X-Api-Key`** (chiavi in `Auth:ApiKeys`); in alternativa è accettato il JWT Bearer. Tutte le route sono sotto `/api`. Serializzazione JSON in **camelCase**.

### Lookup (estesi)
| Metodo | Path | Note |
| --- | --- | --- |
| GET | `/api/lookup/customers` | Ora include `vatNumber`, `taxCode`, `address`, `email` (da `OCRD`). |
| GET | `/api/lookup/projects` | Ora include `cardCode`, `cardName` (cliente del progetto, da `OPMG`→`OCRD`) e `fiscalProjectCode` (codice progetto contabile, `OPMG.FIPROJECT`/UDF `U_SGS_PRJ_PrjCode`; `null` se non associato). Lo stesso campo è incluso anche in `/api/lookup/customers/{cardCode}/projects`. |
| GET | `/api/lookup/projects/{code}/activities` | WBS del progetto. |
| GET | `/api/lookup/resources` | Risorse. |
| GET | `/api/lookup/items?sellableOnly=true` | Articoli (`OITM`): `[{ itemCode, itemName, groupName, active, salesVatGroup }]`. Con `sellableOnly=true` solo `SellItem='Y'` attivi oggi; `active` è calcolato su `validFor`/`frozenFor` con le loro date (un articolo con entrambi a `N` è attivo). |

### Fatture (mirror finanziario + push come bozza)
| Metodo | Path | Descrizione |
| --- | --- | --- |
| GET | `/api/invoices?since=yyyy-MM-dd` | Fatture A/R **definitive** (`OINV`/`INV1`/`INV6`) con righe e scadenzario. Espone `ax360InvoiceId` letto dall'UDF di correlazione. |
| POST | `/api/invoices` | Crea una **BOZZA** di fattura A/R (oggetto `Drafts`, `DocObjectCode = oInvoices`). Idempotente sull'UDF `U_AX360_InvId`. Risposta: `{ success, erpDocId, erpDocNumber, docStatus: "draft" }`. |

### Documenti di vendita (fattura / ordine, bozza / definitivo)
| Metodo | Path | Descrizione |
| --- | --- | --- |
| POST | `/api/sales-documents` | Crea in SAP una fattura (`Invoices`) o un ordine cliente (`Orders`), definitivi o in bozza (`Drafts` con `DocObjectCode` `oInvoices`/`oOrders`). Idempotente su `correlationId` (`U_AX360_InvId`, cercato in `OINV`, `ORDR` e bozze `ODRF` 13/17, con lock per correlationId nel processo). |
| POST | `/api/sales-documents/preview` | Stesse verifiche del POST, restituisce il payload del Service Layer senza scrivere nulla (collaudo). |
| GET | `/api/sales-orders?since=yyyy-MM-dd` | Ordini con `U_AX360_InvId` valorizzato: `[{ correlationId, docEntry, docNum, cardCode, docDate, status: open\|closed\|cancelled, docTotal, openAmount, invoices: [{ docEntry, docNum, docDate }] }]`. `since` filtra su `UpdateDate`; `openAmount` è il residuo imponibile (`RDR1.OpenSum`), zero se l'ordine non è aperto; `invoices` = fatture non annullate con righe `INV1.BaseType = 17` sull'ordine. |
| POST | `/api/fiscal-projects` | Crea un progetto contabile (`OPRJ`) via Service Layer `Projects`. Request `{ name, validFrom, validTo, code }`, response `{ code, created, errorMessage }`. |

**Request di `POST /api/sales-documents`** (camelCase):
```json
{
  "correlationId": "guid-fattura-portale", "portalNumber": "FT-2026-0001",
  "documentKind": "invoice", "posting": "draft",
  "cardCode": "IT00387", "docDate": "2026-09-30", "dueDate": null,
  "comments": "Attività svolte dal 01-09-2026 al 30-09-2026", "projectCode": "25PRJ_2500040",
  "customerReference": { "orderNumber": "AD250169", "orderDate": "2026-08-01", "cig": "BA1A7CF173", "cup": null },
  "lines": [ { "itemCode": "ATT_MTF_CONSULENZA", "description": "…", "quantity": 4.25, "unitPrice": 75.0,
               "unitOfMeasureCode": "HH", "projectCode": "25PRJ_2500040", "costingCode2": "PAS", "costingCode3": "CONS", "vatGroup": null } ],
  "attachments": [ { "fileName": "Comal - Attivita settembre 2026.pdf", "contentBase64": "…" } ]
}
```
**Response:** `{ success, documentKind, status, objectType ("13"/"17"/"112"), docEntry, docNum, docTotal, vatSum, docDueDate, alreadyExisted, attachmentEntry, attachmentError, errorMessage, warnings }`.
HTTP: **201** creato, **200** già esistente (dati del documento REALE: tipo e stato possono differire da quelli chiesti, decide il portale), **400** payload non valido, **403** fattura definitiva con `SapB1:SalesDocuments:AllowPostedInvoices` spento, **500** errore interno o `U_AX360_InvId` assente sulla tabella di destinazione, **502** rifiuto di SAP.

Regole del payload:
- testata: `CardCode`, `DocDate`, `TaxDate = DocDate`, `DocDueDate` **solo se presente** (altrimenti la calcola SAP dalle condizioni di pagamento), `Comments` troncato, `Project`, `NumAtCard = orderNumber`, UDF `U_MTF_FE_ODA/DTORD/CIG/CUP` **solo se esistono** sulla tabella (verifica `SYS.TABLE_COLUMNS` ∪ `CUFD`, con cache), `U_AX360_InvId`, `U_AX360_InvNum`, sconto di testata 0;
- righe: `ItemCode` **obbligatorio** (nessun ripiego su `SapB1:TimeAndMaterialsItemCode`), `ItemDescription` troncata alla colonna, `Quantity > 0`, `UnitPrice`, sconto 0, **nessun `LineTotal`** (i totali si rileggono dalla risposta), `UoMEntry` da `OUOM.UomCode` se l'unità appartiene al gruppo UdM dell'articolo (articolo "manuale": `MeasureUnit` testuale; altrimenti avviso in `warnings`), `ProjectCode` (default: quello di testata), `CostingCode2/3`, `VatGroup` solo se presente;
- allegati: upload `Attachments2` (nome file ASCII con suffisso dal correlationId) e `AttachmentEntry` sul documento; un errore di allegato NON annulla il documento (`attachmentError`);
- le lunghezze delle colonne si leggono da `SYS.TABLE_COLUMNS`; se non leggibile valgono ripieghi prudenti (`Dscription` 100, `NumAtCard` 100, `Comments` 254, `PrjName` 100).

**Codici `OPRJ`:** pattern `SapB1:FiscalProjectCodePattern` (default `PRJ{yy}_{yy}{seq5}`, es. `PRJ26_2600131`) = massimo esistente dell'anno di `validFrom` + 1, letto con `LIKE … ESCAPE ''` (il `_` è un jolly); creazione serializzata nel processo e ricalcolo del codice se SAP risponde "già esistente". Nome troncato alla colonna (100). Con `code` esplicito già esistente: 200 e `created = false`.

### Partitario
| Metodo | Path | Descrizione |
| --- | --- | --- |
| GET | `/api/ledger?customerCode=&since=` | Movimenti di partitario (`JDT1`/`OJDT`) con saldo progressivo. |

### Contabilità generale (conto economico)
| Metodo | Path | Descrizione |
| --- | --- | --- |
| GET | `/api/gl/lines?from=&to=&skip=&take=` | Righe di conto economico nella finestra `[from, to]` (per data di registrazione). Nessun incrementale: il chiamante rilegge la finestra e fa mark-and-sweep (vedi commento sul metodo). `skip` (default `0`) e `take` (default `0`) sono opzionali e retrocompatibili: `take=0` significa **nessuna paginazione**, la finestra intera come oggi. Con `take > 0` la query pagina in modo stabile (l'`ORDER BY RefDate, TransId, Line_ID` è totale, quindi le pagine non si sovrappongono e non perdono righe); `take` è cappato server-side a `5000`. Il chiamante che pagina deve leggere **tutte** le pagine prima di considerare completo il giro, o il mark-and-sweep lato portale cancellerebbe dal mirror le righe non ancora lette. |

### Campi utente (UDF) di correlazione
Creati all'avvio (best-effort, via `UserFieldsMD`) su `OINV`, `ODRF` e `ORDR` **solo se** `SapB1:Bootstrap:UserFields:Enabled = true` (opt-in: modifica i metadati della company). Prima di leggere o scrivere un UDF su una tabella il servizio ne verifica l'esistenza:
- `U_AX360_InvId` — codice interno AX.360 (Invoice.Id): chiave di correlazione stabile, si propaga da bozza a definitivo;
- `U_AX360_InvNum` — numero leggibile AX.360;
- `U_AX360_DocType` — tipo documento per il mirror (canone|manutenzione|servizio|altro).

**Flusso bozza → definitivo:** AX invia la fattura → il servizio crea una bozza con gli UDF valorizzati → l'operatore conferma la bozza in SAP (diventa `OINV`, ereditando gli UDF) → al successivo `GET /api/invoices` AX riconosce la fattura via `ax360InvoiceId` e la marca come `posted`.

> I nomi colonna SAP (`OINV`/`INV1`/`INV6`/`JDT1`/`OJDT`, `OCRD.AdditionalID` per il codice fiscale, `SapB1:DefaultVatGroup` per il gruppo IVA delle righe bozza) seguono lo schema standard: verificare su installazioni con localizzazioni particolari.

---

*Documento allineato al codice del repository AX.SAPB1.Api. Per comportamenti esatti in produzione, verificare configurazione JWT/SAP sul server.*

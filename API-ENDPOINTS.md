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
| GET | `/api/timesheet/billing-state` | Sì | Stato di fatturazione delle righe nella finestra `from`/`to` (ore fatturabili `hours` e lorde `totalHours`) |
| POST | `/api/timesheet` | Sì | Crea timesheet (SAP Service Layer) |
| POST | `/api/timesheet/lite` | Sì | Crea timesheet da payload semplificato; `billableHours` facoltativo |
| POST | `/api/timesheet/lite/preview` | Sì | Come `/lite`, restituisce il payload del Service Layer senza scrivere |
| PUT | `/api/timesheet/{docEntry}` | Sì | Aggiorna timesheet (SAP Service Layer); `DocEntry` URL = body |
| PATCH | `/api/timesheet/{docEntry}/hours` | Sì | Aggiorna **solo** ore lorde/fatturabili di una riga non fatturata (merge a tre vie con il portale) |
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
- **200:** `Timesheet[]`, **comprese le righe annullate** (la query non filtra `Canceled`: serve anche ad altri
  chiamanti). Ogni riga porta il campo additivo **`canceled`** (`bool?`, colonna di sistema `Canceled`): `true` =
  riga annullata (`'Y'`), `false` = attiva (`'N'`), `null` = valore assente o illeggibile. Un servizio precedente non
  manda il campo: per il chiamante vale come `null`. Chi cerca la riga creata da un proprio push (il portale, dopo un
  timeout) scarta **solo** le righe con `canceled = true` e tratta `null`/assente come riga non annullata: altrimenti,
  con un servizio non aggiornato, ogni ricerca risponderebbe "non lo so" e i nuovi tentativi resterebbero fermi. Tutte
  le altre risposte che portano un `Timesheet` (le altre letture, e le risposte di `POST`, `POST /lite` e `PUT`)
  non leggono la colonna e restituiscono sempre `canceled: null`.
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

> Se `TimeNrTot` è valorizzato, questo endpoint scrive anche `U_TimeNrNet = TimeNrTot`: **non** usarlo per righe
> con ore fatturabili ridotte (azzererebbe la riduzione). Per le sole ore usare `PATCH /{docEntry}/hours`.

#### `POST /lite` e `POST /lite/preview`

Creazione da payload semplificato (`TimesheetCreateRequestLite`, guida operativa in
[`TIMESHEET-LITE-INTEGRAZIONE.md`](TIMESHEET-LITE-INTEGRAZIONE.md)); `/preview` restituisce il payload del Service
Layer senza scrivere. Campo facoltativo **`billableHours`** (ore fatturabili): assente/`null` = uguale a `hours`
(comportamento precedente). Scrittura: `U_TimeNrTot = hours`, `U_TimeNrNet = billableHours ?? hours`,
`U_TimeNrNF = hours - U_TimeNrNet` (`U_TimeNrPa` e i campi `*Ori` restano a zero, `U_TimeEnd` resta calcolata sulle
ore lorde).

- **201:** `Timesheet` (solo `/lite`); **200:** `TimesheetServiceLayerPayload` (solo `/preview`)
- **400:** `hours` assente o `<= 0`; `billableHours` fuori da `[0, hours]`; dati anagrafici del progetto incompleti
- **404:** progetto o attività non trovati

#### `PATCH /{docEntry}/hours`

Aggiorna le sole ore di una riga già spinta e non fatturata. Contratto completo nella sezione
[Timesheet del portale AX.360](#timesheet-ore-lorde-e-ore-fatturabili).

#### `GET /billing-state?from=&to=`

Stato di fatturazione delle righe con `U_Date` in `[from, to]` (massimo 400 giorni). Contratto nella sezione
[Timesheet del portale AX.360](#timesheet-ore-lorde-e-ore-fatturabili).

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

### Timesheet: ore lorde e ore fatturabili

SGS (AddOn SAP B1) fattura il T&M dalla tabella `@SGS_PRJ_OTMS` usando **`U_TimeNrNet`** come quantità di riga (e
seleziona solo le righe con `U_TimeNrNet > 0`). Quindi le ore che il portale considera fatturabili devono stare in
`U_TimeNrNet`, non solo le ore lorde: `U_TimeNrTot` = ore lorde, `U_TimeNrNet` = ore fatturabili, `U_TimeNrNF` =
lorde − fatturabili. L'identificativo di riga è sempre **`DocEntry`** (mai `Code`: le due colonne divergono su ~12%
delle righe).

| Metodo | Path | Descrizione |
| --- | --- | --- |
| POST | `/api/timesheet/lite` | Crea la riga. Nuovo campo facoltativo `billableHours` (assente/`null` = `hours`); `0 <= billableHours <= hours`, altrimenti **400**. Risposta 201 con `docEntry`. |
| PATCH | `/api/timesheet/{docEntry}/hours` | Aggiorna **solo** `U_TimeNrTot`/`U_TimeNrNet`/`U_TimeNrNF` di una riga non fatturata, senza sovrascrivere modifiche fatte a mano in SAP (vedi sotto). |
| GET | `/api/timesheet/billing-state?from=&to=` | Stato di fatturazione: `[{ erpDocId, state, invoiceErpDocNumber, invoicedOn, hours, totalHours, erpResourceCode, erpProjectCode, erpActivityCode, workedOn }]`. `hours` = `U_TimeNrNet` (fatturabili), **`totalHours`** = `U_TimeNrTot` (lorde, campo additivo). `hours` assente o illeggibile vale 0 (contratto esistente); `totalHours` assente o illeggibile vale **`null`** ("non lo so": il portale non confronta). |
| GET | `/api/timesheet/employee/{resId}/daterange?startDate=&endDate=` | Righe di una risorsa (usato dal portale per ritrovare una riga dopo un push andato in timeout): ogni `Timesheet` porta `timeNrTot`, `timeNrNet`, `timeNrNF`, `timeNrPa` e il campo additivo **`canceled`** (`true` = `Canceled = 'Y'`, `false` = `'N'`, `null` = assente/illeggibile). Le righe annullate **non** sono filtrate (la query serve anche ad altri chiamanti): chi cerca la riga del proprio push scarta **solo** quelle con `canceled = true` e tratta `null` (o il campo assente, servizio precedente) come riga non annullata. |

**`PATCH /api/timesheet/{docEntry}/hours`** — merge a tre vie: base = ultimo valore spinto dal portale (`expected*`),
nostro = portale (`hours`/`billableHours`), loro = SAP. "Loro" si legge due volte: via ODBC per la prima decisione
(economica, senza sessione del Service Layer) e, se questa dice di scrivere, di nuovo dal Service Layer subito prima
del PATCH (vedi "Finestra fra lettura e scrittura" sotto).

Request:
```json
{ "hours": 4.0, "billableHours": 2.5, "expectedHours": 4.0, "expectedBillableHours": 4.0 }
```

| Campo | Obbligatorio | Significato |
| --- | --- | --- |
| `hours` | **sì**, `> 0` | ore lorde, scritte in `U_TimeNrTot` |
| `billableHours` | **sì**, `0 <= billableHours <= hours` | ore fatturabili, scritte in `U_TimeNrNet` (la quantità che SGS fattura) |
| `expectedHours` | no | ore lorde che il chiamante si aspetta in SAP (l'ultimo valore spinto) |
| `expectedBillableHours` | no | ore fatturabili che il chiamante si aspetta in SAP (l'ultimo valore spinto) |

**`billableHours` è obbligatorio in questo endpoint.** Se manca o è `null` la risposta è 400 `invalid` e la riga non
viene né letta né scritta. A differenza di `POST /lite`, qui l'assenza **non** vale "uguale a `hours`": in un
aggiornamento un campo dimenticato riporterebbe alle ore piene le fatturabili che il capo progetto aveva ridotto, cioè
proprio il guasto che questo endpoint esiste per correggere. `expectedHours`/`expectedBillableHours`, se presenti,
abilitano il controllo di concorrenza, ciascuno sulla sua colonna; assenti o `null` = nessun controllo su quella
colonna.

Risposte, in quest'ordine di valutazione:

| HTTP | `outcome` | Quando |
| --- | --- | --- |
| 400 | `invalid` | `hours` assente o `<= 0`, `billableHours` assente o fuori da `[0, hours]` |
| 404 | `not_found` | nessuna riga con quel `DocEntry`; il corpo c'è **sempre** e porta `outcome = "not_found"` (un 404 senza corpo o senza `outcome` = rotta assente, servizio non aggiornato: vedi sotto) |
| 409 | `canceled` | `Canceled = 'Y'` |
| 409 | `billed` | `U_Status = 'Fatturato'` **oppure** `U_DestEntry` valorizzato (non null e non 0): le righe fatturate non si toccano mai |
| 200 | `unchanged` | in SAP (`U_TimeNrTot`, `U_TimeNrNet`) valgono già (`hours`, `billableHours`) entro 0,001: un nuovo tentativo dopo un timeout riuscito non scrive due volte |
| 409 | `changed_in_erp` | `expected*` presenti e diversi da SAP (tolleranza 0,001): la riga è stata modificata in SAP dopo il push, non si sovrascrive |
| 200 | `updated` | scritti `U_TimeNrTot = hours`, `U_TimeNrNet = billableHours`, `U_TimeNrNF = hours − billableHours` (nessun altro campo) |
| 502 | `error` | il Service Layer ha rifiutato la ricerca della riga o la scrittura (`message` riporta l'errore SAP); anche un 401 sul PATCH finisce qui, senza secondo invio |
| 499 | `error` | il chiamante ha abbandonato la richiesta (timeout) prima del PATCH: **nessuna scrittura**; nessuno legge la risposta, conta il log |
| 500 | — | errore interno (ODBC/Service Layer non raggiungibili): corpo testuale, stato della riga non verificato |

Corpo di 200/409 (e 400/404/502):
```json
{ "outcome": "updated", "currentHours": 4.0, "currentBillableHours": 2.5, "message": null }
```
`currentHours`/`currentBillableHours` = valori in SAP dopo l'operazione (per `updated` i nuovi valori; `null` se
assenti o illeggibili).

**404: riga assente o rotta assente.** Il 404 della riga assente porta **sempre** questo corpo:
```json
{ "outcome": "not_found", "currentHours": null, "currentBillableHours": null, "message": "Riga di timesheet non trovata in SAP" }
```
(il testo di `message` è informativo e può cambiare; il contratto è `outcome`). Un 404 **senza corpo**, o con un corpo
che non porta `outcome` (pagina HTML di IIS o di un proxy, `ProblemDetails`, …), significa invece che la **rotta non
esiste**: servizio non ancora aggiornato o URL base sbagliato, **non** riga mancante. Anche un `{docEntry}` non intero
dà un 404 di rotta (vincolo `:int`). Il chiamante distingue i due casi solo da `outcome`: il portale blocca la riga
su `not_found` (fino alla prossima modifica delle ore) e sul 404 di rotta si limita a ritentare al giro dopo.

Scrittura via Service Layer come `PUT /api/timesheet/{docEntry}`: ricerca della riga con
`SGS_PRJ_OTMS?$filter=DocEntry eq …` per ricavarne il `Code`, poi `PATCH SGS_PRJ_OTMS('{Code}')`. Ogni aggiornamento
è registrato nel log (Information: `DocEntry`, ore da → a); rifiuti e conflitti a livello Warning/Error.

**Finestra fra lettura e scrittura.** Fra la lettura ODBC e il PATCH ci sono la validazione della sessione del
Service Layer (o un login, che può restare appeso) e la ricerca del `Code`: un intervallo non limitato in cui SGS
può fatturare la riga o un utente modificarla. Per questo la riga restituita dalla ricerca (`Canceled`,
`U_Status`, `U_DestEntry`, `U_TimeNrTot`, `U_TimeNrNet`, letti con gli stessi parser tolleranti) viene ridecisa con
la stessa richiesta subito prima del PATCH: se non è più `updated` il PATCH non parte e la risposta è quella nuova
(409 `billed`/`canceled`/`changed_in_erp` o 200 `unchanged`). Una colonna che il Service Layer non espone affatto
prende il valore letto via ODBC. Resta scoperto solo il giro fra la ricerca e il PATCH. Il PATCH non si ripete
dopo un 401 (un nuovo login farebbe invecchiare di nuovo la decisione): torna 502 e il tentativo successivo del
portale rilegge tutto. Il lock per `DocEntry` serializza le richieste dello stesso processo.

**Chiamante andato via.** Il token della richiesta si controlla dopo la lettura ODBC, dopo la sessione, sulla
ricerca e subito prima del PATCH: se il portale è già andato in timeout, non si scrive (499). Senza questo
controllo una scrittura arrivata tardi farebbe trovare al tentativo successivo del portale valori diversi sia
dagli `expected*` sia dalla richiesta, cioè un falso 409 `changed_in_erp`. Resta la corsa "abbandono dopo l'invio
del PATCH", che il ramo `unchanged` copre quando il nuovo tentativo porta gli stessi valori.

**Ore in testo.** `U_TimeNrTot`/`U_TimeNrNet` possono arrivare come testo: si leggono a cultura invariante con il
solo punto decimale. Un testo con la virgola («6,5») è illeggibile (`null`), non 65: meglio "non lo so" (che con
gli `expected*` presenti dà `changed_in_erp`, cioè nessuna scrittura) di un numero dieci volte più grande.

**Ordine di rilascio: prima il servizio, poi il portale.** Un servizio precedente ignora `billableHours` nella
`POST /lite` e scrive `U_TimeNrNet = hours`. Se il portale nuovo registrasse come base il valore che ha *inviato*
invece di quello che SAP ha *scritto*, portale e base coinciderebbero, la riconciliazione non manderebbe mai il
PATCH e SGS fatturerebbe le ore piene sulle righe spinte in quella finestra, anche dopo l'aggiornamento del servizio.
La risposta 201 di `POST /lite` riporta la riga come l'ha restituita SAP (`timeNrTot`, `timeNrNet`, `timeNrNF`):
è quella la base da registrare, con il valore inviato solo come riserva se la risposta non la porta.

**Punto aperto (verificato sul sorgente SGS, da decidere prima del rilascio): campi orari non allineati.** Il
servizio scrive solo i campi numerici delle ore: la creazione lite lascia `U_TimeNF` (ore non fatturabili in
formato orario) vuoto anche quando `U_TimeNrNF` > 0, e il PATCH cambia `U_TimeNrTot` senza spostare `U_TimeEnd`
(09:00 + le ore lorde di prima). Il form timesheet dell'AddOn (`FormTimesheet.CalcolaTotali`) ricalcola
`U_TimeNrTot = U_TimeEnd − U_TimeStart − U_TimePa`, `U_TimeNrNF` da `U_TimeNF` e `U_TimeNrNet = U_TimeNrTot −
U_TimeNrNF`, ma **solo** quando un utente modifica in quel form uno dei campi orari della riga (inizio, fine,
totale, pausa, non fatturabili): aprire la riga, o salvarla senza toccare quei campi, non ricalcola nulla. Se il
ricalcolo parte su una riga con fatturabili ridotte, `U_TimeNrNF` viene rifatto da `U_TimeNF`, che il servizio lascia
vuoto (quindi zero, salvo che l'utente compili proprio quel campo), `U_TimeNrNet` torna alle ore piene e SGS le fattura; se un PATCH aveva cambiato le ore lorde, anche `U_TimeNrTot` torna a quelle
ricavate da `U_TimeEnd`.

**Il portale non se ne accorge da solo.** Una modifica fatta in SAP non genera di per sé nessun `changed_in_erp`: il
409 esiste solo come risposta a un PATCH, e il portale manda il PATCH solo per le righe le cui ore **nel portale**
sono cambiate rispetto all'ultimo valore spinto (la base del merge). Finché le ore della rendicontazione nel portale
restano quelle, nessun PATCH parte, e nemmeno la lettura dello stato di fatturazione (`GET /billing-state`) lo
segnala: per una riga di cui il portale conosce già l'ultimo valore spinto — tutte quelle spinte con le fatturabili —
uno scostamento di SAP da quel valore non produce né un avviso né una scrittura. SGS fattura quindi le ore piene
senza alcuna segnalazione. Il portale se ne accorge solo se **dopo** il ricalcolo le ore cambiano di nuovo nel
portale: il PATCH successivo porta come `expected*` l'ultimo valore spinto, SAP non lo ha più e la risposta è 409
`changed_in_erp` (riga bloccata e segnalata, nessuna sovrascrittura); oppure 200 `unchanged` se le nuove ore del
portale coincidono con quelle ricalcolate in SAP, o 409 `billed` se nel frattempo SGS ha già fatturato la riga.

Estensione possibile, fuori dal contratto attuale: scrivere `U_TimeNF` (hh:mm:ss delle ore non
fatturabili) nella creazione lite e nel PATCH, e `U_TimeEnd` nel PATCH quando cambiano le ore lorde; va provata
prima su una company di test (formato del campo orario nel Service Layer, e `MapSapResponseToTimesheet` che oggi
legge `U_TimeNF` come intero).

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
| GET | `/api/sales-documents/by-correlation/{correlationId}` | Stato del documento creato per la correlazione: `{ found, documentKind, status, docEntry, docNum, cancelled }`. Sempre 200 (`found = false` se non c'è: un 404 vuol dire servizio non aggiornato). Il portale lo chiede prima di annullare un suo documento già in SAP. Una bozza già trasformata nel definitivo (`ODRF.DocStatus = 'C'`) non vince mai sul definitivo, nemmeno se annullato; se è l'unica traccia risponde `status = posted`, non annullato. Attende una creazione in corso per la stessa correlazione; per 15 minuti dopo una creazione senza risposta dal Service Layer, invece di `found = false` risponde 500 (stato non verificabile). |
| GET | `/api/sales-orders?since=yyyy-MM-dd` | Ordini con `U_AX360_InvId` valorizzato: `[{ correlationId, docEntry, docNum, cardCode, docDate, status: open\|closed\|cancelled, docTotal, openAmount, invoices: [{ docEntry, docNum, docDate }] }]`. `since` filtra su `UpdateDate`; `openAmount` è il residuo imponibile (`RDR1.OpenSum`), zero se l'ordine non è aperto; `invoices` = fatture non annullate con righe `INV1.BaseType = 17` sull'ordine. |
| POST | `/api/fiscal-projects` | Crea un progetto contabile (`OPRJ`) via Service Layer `Projects`. Request `{ name, validFrom, validTo, code, codeYear, idempotencyKey }`, response `{ code, created, errorMessage }`. |

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
- testata: `CardCode`, `DocDate`, `TaxDate = DocDate`, `DocDueDate`: per la fattura **solo se presente** (altrimenti la calcola SAP dalle condizioni di pagamento), per l'ordine (definitivo o bozza) è la data di consegna obbligatoria e vale `dueDate ?? docDate`, `Comments` troncato, `Project`, `NumAtCard = orderNumber`, UDF `U_MTF_FE_ODA/DTORD/CIG/CUP` **solo se esistono** sulla tabella (verifica `SYS.TABLE_COLUMNS` ∪ `CUFD`, con cache), `U_AX360_InvId`, `U_AX360_InvNum`, sconto di testata 0;
- righe (ordine delle proprietà: `ItemCode`, poi `UoMEntry`/`MeasureUnit`, poi descrizione, quantità e prezzo: il cambio di UdM rilegge il prezzo dal listino e non deve sovrascrivere quello del portale): `ItemCode` **obbligatorio** (nessun ripiego su `SapB1:TimeAndMaterialsItemCode`), `ItemDescription` troncata alla colonna, `Quantity > 0`, `UnitPrice`, sconto 0, **nessun `LineTotal`** (i totali si rileggono dalla risposta), `UoMEntry` da `OUOM.UomCode` se l'unità appartiene al gruppo UdM dell'articolo (articolo "manuale": `MeasureUnit` testuale; altrimenti avviso in `warnings`), `ProjectCode` (default: quello di testata), `CostingCode2/3`, `VatGroup` solo se presente;
- allegati: **opt-in** (`SapB1:SalesDocuments:Attachments:Enabled`, assente = spento), upload `Attachments2` (nome file ASCII con suffisso dal correlationId) e `AttachmentEntry` sul documento; un errore di allegato NON annulla il documento (`attachmentError`); se un tentativo precedente dello stesso documento ha già caricato gli stessi file (SAP ha rifiutato il documento dopo l'upload) la voce `ATC1` si riusa;
- le lunghezze delle colonne si leggono da `SYS.TABLE_COLUMNS`; se non leggibile valgono ripieghi prudenti (`Dscription` 100, `NumAtCard` 100, `Comments` 254, `PrjName` 100).

**Codici `OPRJ`:** pattern `SapB1:FiscalProjectCodePattern` (default `PRJ{yy}_{yy}{seq5}`, es. `PRJ26_2600131`) = massimo esistente dell'anno di CREAZIONE + 1 (`codeYear` se indicato, altrimenti l'anno di oggi a Roma: MAI l'anno di `validFrom`, che il portale retrodata apposta), letto con `LIKE … ESCAPE ''` (il `_` è un jolly); creazione serializzata nel processo e ricalcolo del codice se SAP risponde "già esistente". Nome troncato alla colonna (100). Con `code` esplicito già esistente: 200 e `created = false`. Con lo stesso `idempotencyKey` (l'Id del progetto del portale) ripetuto entro 24 ore, senza riavvii del servizio: 200, il codice creato la prima volta e `created = false` (una risposta persa non crea un secondo OPRJ).

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

# Integrazione endpoint `POST /api/Timesheet/lite`

Questa guida descrive in modo operativo come un software esterno deve chiamare l'endpoint "lite" per creare un timesheet.

## Endpoint

- **Metodo**: `POST`
- **Path**: `/api/Timesheet/lite`
- **Content-Type**: `application/json`

URL completo (esempio):

`https://<host-api>/api/Timesheet/lite`

---

## Modello di input (body JSON)

Il body deve rispettare il modello `TimesheetCreateRequestLite`.

```json
{
  "date": "2026-03-26T00:00:00",
  "resId": "EMP001",
  "project": "PRJ-0001",
  "activityId": "ACT-010",
  "hours": 8.0,
  "desc": "Analisi requisiti e allineamento tecnico",
  "billableHours": 6.0
}
```

### Campi richiesti

- `date` (`DateTime`, obbligatorio): data attività.
- `resId` (`string`, obbligatorio): identificativo risorsa/dipendente.
- `project` (`string`, obbligatorio): codice progetto.
- `activityId` (`string`, obbligatorio): codice attività del progetto.
- `hours` (`decimal`, obbligatorio): ore da registrare, **deve essere > 0**.
- `desc` (`string`, obbligatorio): descrizione attività.

> Nota: oltre al vincolo di obbligatorietà, il controller applica una validazione esplicita su `hours`: se `null` o `<= 0` la chiamata viene rifiutata.

### Campi facoltativi

- `billableHours` (`decimal`, facoltativo): ore **fatturabili** della riga. Assente o `null` = uguale a `hours`
  (comportamento precedente: chi non lo invia non cambia nulla). Vincolo `0 <= billableHours <= hours`, altrimenti 400.

Come vengono scritte le ore in SAP (`@SGS_PRJ_OTMS`):

| Campo SAP | Valore |
|-----------|--------|
| `U_TimeNrTot` | `hours` (ore lorde) |
| `U_TimeNrNet` | `billableHours ?? hours` (ore fatturabili: è la quantità che SGS usa in fattura) |
| `U_TimeNrNF` | `hours - U_TimeNrNet` (ore non fatturabili) |
| `U_TimeEnd` | 09:00 + `hours` (tempo lavorato, sulle ore lorde) |

`U_TimeNrPa` e i campi `*Ori` restano a zero. Per correggere le ore di una riga già creata e non ancora fatturata
usare `PATCH /api/timesheet/{docEntry}/hours` (contratto completo in `API-ENDPOINTS.md`). Due differenze da tenere
presenti rispetto a questo endpoint:

- nel PATCH **`billableHours` è obbligatorio** (come `hours`): se manca o è `null` la risposta è 400 `invalid`, non
  "uguale a `hours`" — in un aggiornamento un campo dimenticato riporterebbe alle ore piene le fatturabili ridotte;
- il 404 della riga assente porta **sempre** il corpo
  `{"outcome":"not_found","currentHours":null,"currentBillableHours":null,"message":"Riga di timesheet non trovata in SAP"}`
  (il testo di `message` è informativo). Un 404 **senza corpo** o **senza `outcome`** vuol dire invece che la rotta
  non esiste: servizio non ancora aggiornato, non riga mancante.

Per ritrovare una riga dopo una creazione andata in timeout (la `POST` può essere arrivata a SAP mentre il chiamante
smetteva di aspettare) si usa `GET /api/timesheet/employee/{resId}/daterange?startDate=&endDate=`. Quella ricerca
restituisce **anche le righe annullate**: ogni riga porta il campo additivo `canceled` (`true` = annullata,
`Canceled = 'Y'`; `false` = attiva, `'N'`; `null` = assente o illeggibile). Le righe con `canceled = true` non sono la
riga creata dal push e vanno scartate; `null` (o il campo assente, servizio precedente) vale come riga non annullata,
altrimenti con un servizio non aggiornato nessuna ricerca troverebbe più la riga.

> **Punto aperto: `U_TimeNF` non viene scritto.** Con `billableHours < hours` la riga ha `U_TimeNrNF` > 0 ma
> `U_TimeNF` (lo stesso dato in formato orario) vuoto. Il form timesheet dell'AddOn SGS ricalcola le ore numeriche
> dai campi orari (`U_TimeNrNet = U_TimeEnd − U_TimeStart − U_TimePa − U_TimeNF`) **solo** quando un utente modifica
> in quel form uno dei campi orari della riga (inizio, fine, totale, pausa o non fatturabili): su una riga creata da
> qui la riduzione delle ore fatturabili andrebbe persa e SGS fatturerebbe le ore piene. Aprire la riga, o salvarla
> senza toccare quei campi, non ricalcola nulla.
>
> Il portale **non** se ne accorge da solo: non riceve nessun `changed_in_erp` al momento del ricalcolo, perché il
> 409 è solo la risposta a un `PATCH /api/timesheet/{docEntry}/hours`, e il portale manda quel PATCH solo quando le
> ore della rendicontazione cambiano nel portale rispetto all'ultimo valore spinto. Finché nel portale non cambiano,
> SGS fattura le ore piene senza alcuna segnalazione (nemmeno dalla lettura dello stato di fatturazione). Se ne
> accorge solo se **dopo** il ricalcolo le ore cambiano di nuovo nel portale: il PATCH che parte allora trova in SAP
> valori diversi dall'ultimo spinto e riceve 409 `changed_in_erp` (nessuna sovrascrittura; 200 `unchanged` se le nuove
> ore coincidono con quelle ricalcolate, 409 `billed` se la riga è già fatturata). Dettagli e possibile estensione in
> `API-ENDPOINTS.md`, sezione "Timesheet: ore lorde e ore fatturabili".
>
> La risposta 201 riporta la riga come l'ha scritta SAP (`timeNrTot`, `timeNrNet`, `timeNrNF`): chi integra deve
> registrare quei valori, non quelli inviati. Un servizio non ancora aggiornato ignora `billableHours` e scrive
> `timeNrNet = hours`: la risposta lo dice.

---

## Comportamento server (risoluzione automatica dipendenze)

Con il payload "lite" non devi inviare i dati anagrafici completi del timesheet.  
Il server li risolve automaticamente:

1. Cerca il progetto tramite `project`.
2. Recupera le attività del progetto.
3. Cerca l'attività con codice uguale a `activityId` (confronto case-insensitive).
4. Verifica che il progetto abbia dati anagrafici minimi completi (`CardCode` e `Name`).
5. Costruisce e crea il timesheet finale su SAP B1 Service Layer.

---

## Risposte dell'endpoint

## `201 Created` (successo)

Restituisce un oggetto `Timesheet` completo (JSON) e imposta anche:

- Header `Location` verso `GET /api/Timesheet/{docEntry}`

Struttura del payload di risposta (`Timesheet`):

- `docEntry` (`int?`)
- `code` (`string?`)
- `resId` (`string?`)
- `cardCode` (`string?`)
- `cardName` (`string?`)
- `refId` (`string?`)
- `refData` (`string?`)
- `project` (`string?`)
- `projectName` (`string?`)
- `subProject` (`string?`)
- `activity` (`string?`)
- `activityId` (`string?`)
- `subActivity` (`string?`)
- `activityName` (`string?`)
- `date` (`DateTime`)
- `timeStart` (`int?`)
- `timeEnd` (`int?`)
- `timePa` (`int?`)
- `timeNF` (`int?`)
- `timeNrPa` (`decimal?`)
- `timeNrNF` (`decimal?`)
- `timeNrTot` (`decimal?`)
- `timeNrNet` (`decimal?`)
- `descExt` (`string?`)
- `descInt` (`string?`)
- `status` (`string?`)
- `canceled` (`bool?`): campo additivo; in questa risposta è sempre `null` (la colonna `Canceled` è letta solo
  dalla ricerca per risorsa e date `GET /api/timesheet/employee/{resId}/daterange`)

Esempio di risposta:

```json
{
  "docEntry": 12345,
  "code": "TS-2026-000123",
  "resId": "EMP001",
  "cardCode": "C0001",
  "cardName": "Cliente Demo",
  "refId": null,
  "refData": null,
  "project": "PRJ-0001",
  "projectName": "Progetto Demo",
  "subProject": null,
  "activity": "ACT-010",
  "activityId": "ACT-010",
  "subActivity": null,
  "activityName": "Analisi",
  "date": "2026-03-26T00:00:00",
  "timeStart": null,
  "timeEnd": null,
  "timePa": null,
  "timeNF": null,
  "timeNrPa": null,
  "timeNrNF": 2.0,
  "timeNrTot": 8.0,
  "timeNrNet": 6.0,
  "descExt": "Analisi requisiti e allineamento tecnico",
  "descInt": null,
  "status": "Aperto",
  "canceled": null
}
```

## `400 Bad Request`

Casi principali:

- payload non valido rispetto al modello (`ModelState` non valido);
- `hours` assente o minore/uguale a zero:
  - testo risposta: `"Il campo Hours deve essere maggiore di zero"`;
- `billableHours` negativo o maggiore di `hours`:
  - testo risposta: `"Il campo BillableHours deve essere compreso fra 0 e Hours (<hours>): ricevuto <billableHours>"`;
- progetto trovato ma dati anagrafici incompleti:
  - testo risposta: `"Dati anagrafici incompleti per il progetto {project}"`.

## `404 Not Found`

- Progetto non trovato:
  - `"Progetto {project} non trovato"`
- Attività non trovata per il progetto:
  - `"Attività {activityId} non trovata per il progetto {project}"`

## `500 Internal Server Error`

- errore interno non gestito durante il flusso di creazione:
  - `"Errore interno del server durante la creazione del timesheet lite"`

---

## Esempio completo chiamata HTTP

```bash
curl -X POST "https://<host-api>/api/Timesheet/lite" \
  -H "Content-Type: application/json" \
  -d "{
    \"date\": \"2026-03-26T00:00:00\",
    \"resId\": \"EMP001\",
    \"project\": \"PRJ-0001\",
    \"activityId\": \"ACT-010\",
    \"hours\": 8.0,
    \"desc\": \"Analisi requisiti e allineamento tecnico\"
  }"
```

---

## Note operative per software esterno

- Inviare sempre `date` in formato ISO 8601 (`yyyy-MM-ddTHH:mm:ss`).
- Considerare `activityId` come codice attività nel contesto del progetto.
- Gestire esplicitamente gli errori `400` e `404` mostrando il messaggio testuale ricevuto.
- Dopo un `201`, usare `docEntry` restituito per eventuali operazioni successive (`GET`, `PUT`).

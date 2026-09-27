# Guida all'Installazione e Configurazione

## Prerequisiti

### 1. .NET 8.0 SDK
- Scarica e installa .NET 8.0 SDK da: https://dotnet.microsoft.com/download/dotnet/8.0
- Verifica l'installazione con: `dotnet --version`

### 2. Driver ODBC per SAP HANA
- Scarica il driver ODBC per SAP HANA dal sito SAP
- Installa il driver sul sistema
- Verifica l'installazione nel Gestore DSN ODBC di Windows

### 3. SAP Business One
- SAP Business One installato e configurato
- Service Layer abilitato e accessibile
- UDO Timesheet configurato con i campi richiesti

## Configurazione

### 1. Configurazione ODBC
1. Apri il **Gestore DSN ODBC** di Windows
2. Crea una nuova **System DSN** per SAP HANA
3. Configura i parametri di connessione:
   - **Driver**: SAP HANA
   - **Server**: [indirizzo-server]:30015
   - **Database**: [nome-database]
   - **User ID**: [username]
   - **Password**: [password]

### 2. Configurazione SAP Business One Service Layer
1. Verifica che il Service Layer sia abilitato in SAP B1
2. Annota l'URL del Service Layer (es: https://server:50000/b1s/v2)
3. Verifica le credenziali di accesso

### 3. Configurazione UDO Timesheet
In SAP Business One, crea un UDO chiamato "TIMESHEET" con i seguenti campi:

| Campo | Tipo | Obbligatorio | Descrizione |
|-------|------|--------------|-------------|
| U_Date | Date | Sì | Data del timesheet |
| U_EmployeeId | Text | Sì | ID del dipendente |
| U_ProjectId | Text | Sì | ID del progetto |
| U_ActivityId | Text | Sì | ID dell'attività |
| U_Hours | Numeric | Sì | Ore lavorate |
| U_Description | Text | No | Descrizione del lavoro |
| U_Status | Text | No | Stato del timesheet |

> Sull'impianto MTF la tabella reale è `@SGS_PRJ_OTMS` (UDO dell'AddOn SGS), indirizzata dal Service Layer per `Code`
> ma identificata verso il portale AX.360 per `DocEntry`. Campi ore scritti dal servizio: `U_TimeNrTot` = ore lorde,
> `U_TimeNrNet` = ore **fatturabili** (è la quantità che SGS usa in fattura, solo righe con `U_TimeNrNet > 0`),
> `U_TimeNrNF` = lorde − fatturabili. Una riga con `U_Status = 'Fatturato'` o `U_DestEntry` valorizzato non viene
> mai modificata dal servizio (`PATCH /api/timesheet/{docEntry}/hours` risponde 409 `billed`).

### 4. Configurazione dell'Applicazione
1. Modifica `appsettings.json` o `appsettings.Development.json`
2. Aggiorna la stringa di connessione ODBC
3. Configura i parametri del Service Layer

## Test della Configurazione

### 1. Test ODBC
```bash
# Testa la connessione ODBC
dotnet run --environment Development
```

### 2. Test Service Layer
```bash
# Verifica l'accesso al Service Layer
curl -X POST https://your-server:50000/b1s/v2/Login \
  -H "Content-Type: application/json" \
  -d '{"CompanyDB":"your-db","UserName":"your-user","Password":"your-password"}'
```

### 3. Test API
Una volta avviata l'applicazione:
- Swagger UI: https://localhost:7001/swagger
- Test endpoint: https://localhost:7001/api/timesheet

## Risoluzione Problemi

### Errore: "Driver not found"
- Verifica che il driver ODBC sia installato
- Controlla il nome del driver nella stringa di connessione

### Errore: "Connection failed"
- Verifica l'indirizzo del server SAP HANA
- Controlla le credenziali
- Verifica che la porta 30015 sia aperta

### Errore: "Service Layer not accessible"
- Verifica che il Service Layer sia abilitato
- Controlla l'URL del Service Layer
- Verifica le credenziali di accesso

### Errore: "UDO not found"
- Verifica che l'UDO TIMESHEET sia configurato
- Controlla i nomi dei campi UDO
- Verifica i permessi dell'utente

## Sicurezza

### In Produzione
1. **Gestione Credenziali**: Usa Azure Key Vault o simili
2. **HTTPS**: Configura certificati SSL
3. **Autenticazione**: Implementa autenticazione API
4. **Autorizzazione**: Configura autorizzazioni appropriate
5. **Logging**: Configura logging sicuro

### Best Practices
- Non committare mai credenziali nel codice
- Usa variabili d'ambiente per le configurazioni sensibili
- Implementa rate limiting per le API
- Configura CORS appropriatamente
- Monitora l'accesso alle API

## Supporto

Per problemi tecnici:
1. Controlla i log dell'applicazione
2. Verifica la configurazione ODBC
3. Testa la connessione al Service Layer
4. Contatta il supporto SAP se necessario

## Distribuzione in Produzione (Windows Server)

### Panoramica
Questa API .NET 9 espone endpoint REST e dipende da:
- **ODBC** verso SAP HANA (`HDBODBC`) e/o SQL Server (opzionale)
- **SAP Business One Service Layer** via HTTPS

Sono supportati due modelli di hosting:
- IIS con ASP.NET Core Module (ANCM)
- Servizio Windows tramite `sc.exe` o NSSM

### 1) Preparazione server
- **Sistema**: Windows Server 2019/2022 con aggiornamenti.
- **.NET Runtime**: installa .NET 9 ASP.NET Hosting Bundle.
  - Download: `https://dotnet.microsoft.com/en-us/download/dotnet/9.0`
  - Verifica: `dotnet --info`.
- **Driver ODBC**:
  - SAP HANA Client (include `HDBODBC`). Assicurati che l'architettura (x64) coincida con il processo dell'app.
  - Facoltativo: ODBC Driver 17/18 for SQL Server (se usi `ConnectionStrings:SqlServer`).
- **Certificati**: importa il certificato server usato da SAP B1 Service Layer nella `Local Computer\Trusted Root` o `Intermediate` per evitare warning; in alternativa, lascia la bypass SSL già presente in `Program.cs` (sconsigliato in produzione).
- **Firewall**: apri le porte per l'API (es. 80/443 o porta custom) e assicurati l’uscita verso l’host e la porta del Service Layer (es. 50000).

### 2) Configurazione applicazione
Preferisci variabili d’ambiente su `appsettings.json` per credenziali/host.

- Chiavi principali (case-insensitive con `__` per i separatori):
  - `ConnectionStrings__DefaultDatabase`
  - `ConnectionStrings__SqlServer` (se usata)
  - `SapB1__ServiceLayerUrl` (es. `https://srv-hana01-srv:50000/b1s/v1/`)
  - `SapB1__CompanyDB`
  - `SapB1__UserName`
  - `SapB1__Password`

> **Attenzione — più istanze sullo stesso server.** Le variabili a livello `Machine` sono ereditate da TUTTI i servizi dell'host: un `SapB1__CompanyDB` impostato così per un'istanza di test farebbe puntare anche la produzione alla company di test (e un `ASPNETCORE_URLS` di macchina forzerebbe la stessa porta a entrambe). Con più istanze si configura ciascuna col proprio `appsettings.json` accanto all'exe (vedi §11), mai con variabili `Machine`.

Esempio PowerShell (scope sistema):
```powershell
[Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultDatabase","Driver={HDBODBC};ServerNode=<hana-host>:30015;UID=<user>;PWD=<pwd>;","Machine")
[Environment]::SetEnvironmentVariable("SapB1__ServiceLayerUrl","https://<sap-server>:50000/b1s/v1/","Machine")
[Environment]::SetEnvironmentVariable("SapB1__CompanyDB","<db>","Machine")
[Environment]::SetEnvironmentVariable("SapB1__UserName","<user>","Machine")
[Environment]::SetEnvironmentVariable("SapB1__Password","<strong-password>","Machine")
```
Riavvia IIS/servizio dopo le modifiche.

Note sicurezza:
- Evita credenziali in `appsettings*.json`. Usa secret store/Key Vault quando possibile.
- Valuta di rimuovere il bypass SSL in `Program.cs` in produzione.

### Esempio configurazione Kestrel in appsettings.json
Puoi configurare l’endpoint HTTPS e la porta direttamente nel file di configurazione (alternativa alle variabili d’ambiente):

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:7226"
      }
    }
  }
}
```

Con certificato PFX:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:7226",
        "Certificate": {
          "Path": "C:\\certs\\api.pfx",
          "Password": "<PASSWORD>"
        }
      }
    }
  }
}
```

Con certificato dal Windows Certificate Store:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:7226",
        "Certificate": {
          "Subject": "CN=api.tuodominio.it",
          "Store": "My",
          "Location": "LocalMachine"
        }
      }
    }
  }
}
```

Nota: per evitare l’avviso di redirect HTTPS, imposta anche `ASPNETCORE_HTTPS_PORT=7226` (o aggiungi un binding https esplicito come sopra). In produzione, usa un certificato valido e non il dev-certs.

### 3) Distribuzione su IIS
1. Installa IIS e ASP.NET Core Hosting Bundle.
2. Crea una cartella, es. `C:\inetpub\AX.SAPB1.Api` e copia i file pubblicati (`dotnet publish -c Release`).
3. In IIS Manager:
   - Crea un nuovo Application Pool (No Managed Code, Integrated, 64-bit). Abilita `Start Automatically`.
   - Crea un nuovo Sito o App sotto un sito esistente, impostando la Physical Path alla cartella pubblicata.
   - Associa il nuovo Application Pool.
   - Configura binding: HTTP/HTTPS, host header e certificato (per HTTPS).
4. Concedi permessi di lettura/esecuzione alla Identity dell’App Pool sulla cartella.
5. Variabili d’ambiente: se non configurate a livello macchina, impostale in web.config o a livello di App Pool (Advanced Settings > EnvironmentVariables).
6. Verifica avvio: naviga `/swagger` e prova gli endpoint di `Timesheet` e `Lookup`.

Pubblicazione da CLI (sul server o in CI):
```powershell
 dotnet publish .\AX.SAPB1.Api\AX.SAPB1.Api.csproj -c Release -o C:\inetpub\AX.SAPB1.Api\publish
```
Punta IIS alla cartella `publish`.

### 4) Distribuzione come Servizio Windows (alternativa)
1. Pubblica self-contained o framework-dependent:
```powershell
 dotnet publish .\AX.SAPB1.Api\AX.SAPB1.Api.csproj -c Release -o C:\Services\AX.SAPB1.Api
```
2. Crea il servizio (NSSM consigliato) oppure `sc.exe` con `pwsh`/`dotnet`:
- Con NSSM:
  - `nssm install AX.SAPB1.Api`
  - Path: `C:\Program Files\dotnet\dotnet.exe`
  - Arguments: `C:\Services\AX.SAPB1.Api\AX.SAPB1.Api.dll`
  - Startup: Automatic; Imposta variabili d’ambiente nella scheda `Environment`.
- Con `sc.exe` (solo self-contained EXE):
```powershell
 sc.exe create AX.SAPB1.Api binPath= "C:\Services\AX.SAPB1.Api\AX.SAPB1.Api.exe" start= auto
 sc.exe start AX.SAPB1.Api
```
3. Configura HTTPS per il servizio (senza dev-cert) con certificato self-signed in `LocalMachine`:
```powershell
# PowerShell come Amministratore
$cert = New-SelfSignedCertificate `
  -DnsName "localhost",$env:COMPUTERNAME `
  -CertStoreLocation "Cert:\LocalMachine\My" `
  -FriendlyName "AX.SAPB1.Api SelfSigned" `
  -NotAfter (Get-Date).AddYears(2) `
  -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256

$pwd = ConvertTo-SecureString "CHANGE_ME_STRONG_PASSWORD" -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath "C:\Services\AX.SAPB1.Api\sgs-api-https.pfx" -Password $pwd
```
4. In `C:\Services\AX.SAPB1.Api\appsettings.json` configura Kestrel HTTPS con PFX esplicito:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:7226",
        "Certificate": {
          "Path": "C:\\Services\\AX.SAPB1.Api\\sgs-api-https.pfx",
          "Password": "CHANGE_ME_STRONG_PASSWORD"
        }
      }
    }
  }
}
```
5. (Consigliato) importa il `.cer` in `LocalMachine\Root` per evitare warning TLS sui client interni:
```powershell
Export-Certificate -Cert $cert -FilePath "C:\Services\AX.SAPB1.Api\sgs-api-self.cer"
Import-Certificate -FilePath "C:\Services\AX.SAPB1.Api\sgs-api-self.cer" -CertStoreLocation "Cert:\LocalMachine\Root"
```
6. Log on account: usa un account con permessi minimi e accesso ai driver ODBC.
7. Riavvia e verifica:
```powershell
sc.exe start AX.SAPB1.Api
sc.exe queryex AX.SAPB1.Api
netstat -ano | findstr :7226
```
8. Controlla i log (Event Viewer > Windows Logs > Application).

### 5) ODBC verso SAP HANA e SQL Server
- Il codice usa direttamente connection string ODBC; non è necessario creare DSN, ma assicurati che:
  - Il driver `HDBODBC` sia installato e nel PATH.
  - La porta HANA (tipicamente 30015) sia raggiungibile.
  - Per SQL Server, installa `ODBC Driver 17/18 for SQL Server` e apri la porta 1433 se richiesto.
- Test rapido:
```powershell
 Test-NetConnection <hana-host> -Port 30015
 Test-NetConnection <sql-host> -Port 1433
```

### 6) HTTPS, CORS e Sicurezza
- L’app abilita CORS permissivo (`AllowAnyOrigin`). In produzione, limita origini/headers/metodi o usa `WithOrigins("https://<domain>")`.
- HTTPS: configura binding e certificato in IIS o esegui dietro un reverse proxy con TLS terminato.
- Rimuovi o limita Swagger in produzione (attualmente abilitato solo in Development).
- Proteggi gli endpoint con autenticazione/authorization se esposti pubblicamente.

### 7) Variabili ambiente per logging
Imposta livelli di log:
```powershell
[Environment]::SetEnvironmentVariable("Logging__LogLevel__Default","Information","Machine")
[Environment]::SetEnvironmentVariable("Logging__LogLevel__Microsoft.AspNetCore","Warning","Machine")
[Environment]::SetEnvironmentVariable("Logging__LogLevel__AX.SAPB1.Api","Information","Machine")
```

### 8) Health check e verifica
- Verifica processo in ascolto: `netstat -ano | findstr :<porta>`.
- Verifica endpoint: `GET https://<host>/swagger` e una chiamata a `GET /api/timesheet`.

### 9) Troubleshooting specifico
- "SSL certificate validation bypassed" nei log: indica che è attivo il bypass SSL. Installare CA corrette o rimuovere il bypass in `Program.cs`.
- Errore servizio Windows `1067` con messaggio ".NET Runtime 1026: Unable to configure HTTPS endpoint":
  - il servizio non può usare il developer certificate dell'utente interattivo;
  - configura un certificato esplicito in `Kestrel:Endpoints:Https:Certificate` (PFX consigliato);
  - controlla eventuali override globali: `ASPNETCORE_URLS` in `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` può forzare endpoint inattesi.
- "Driver not found": controlla versione/architettura del driver ODBC.
- "Login failed Service Layer": conferma `SapB1:CompanyDB`, `UserName`, `Password` e raggiungibilità dell’URL.
- Timeouts/401 dal Service Layer: il servizio gestisce il retry; controlla scadenza sessione e orologi NTP.

### 10) Aggiornamenti/Rollback
- Mantieni versioni pubblicate in `C:\inetpub\AX.SAPB1.Api\releases\<version>`.
- Usa uno swap atomico del path IIS o symlink aggiornando la `Physical Path`.
- Conserva backup dell’`appsettings.Production.json` o delle variabili d’ambiente prima degli update.


### 11) Seconda istanza di test su una company copiata (es. `SBO_MTF_AXTEST`)

Serve a collaudare la fatturazione dal portale (documenti di vendita, ordini, progetti contabili) su una copia
della company, senza toccare la produzione. È un **secondo servizio Windows**, con la sua cartella e il suo
`appsettings.json`: la stessa build, nessuna modifica al codice.

**Perché un processo separato e non un parametro.** La company si sceglie con una sola chiave,
`SapB1:CompanyDB`, che pilota sia il Login del Service Layer sia lo schema di TUTTE le query ODBC (ogni tabella è
qualificata come `"<CompanyDB>"."OINV"`, nessuno schema è scritto nel codice). La cache della sessione Service
Layer però è per utente, non per company: un processo serve una company sola.

**Prerequisiti sulla company copiata** (lato SAP, prima di avviare l'istanza):
- la copia deve essere registrata in `SBOCOMMON` (copia/ripristino con gli strumenti SAP B1): copiare lo schema
  HANA non basta, il Login con `CompanyDB = SBO_MTF_AXTEST` fallirebbe;
- ragione sociale cambiata (`OADM.CompnyName`) così che nessuno la scambi per la produzione;
- l'utente del Service Layer (`SapB1:UserName`) esiste nella copia e ha la licenza;
- **cartella allegati**: la copia eredita `OADP.AttachPath` della produzione. Finché non punta a una cartella
  dedicata (visibile al Service Layer, che su HANA gira su Linux) lasciare spento `SapB1:SalesDocuments:Attachments:Enabled`
  (è il default: chiave assente = spento), altrimenti i PDF di test finirebbero nella cartella allegati di produzione.

**Cartella e configurazione.** Es. `C:\Services\AX.SAPB1.Api.AxTest`, con l'exe e un `appsettings.json` proprio
(`deploy.ps1` copia solo l'exe e non lo sovrascrive). Differenze rispetto alla produzione:

```json
{
  "ConnectionStrings": { "DefaultDatabase": "Driver={HDBODBC};ServerNode=<hana-host>:30015;UID=<utente>;PWD=<password>;" },
  "SapB1": {
    "ServiceLayerUrl": "https://<hana-host>:50000/b1s/v1/",
    "CompanyDB": "SBO_MTF_AXTEST",
    "UserName": "<utente SL>",
    "Password": "<password SL>",
    "Write": { "Enabled": false },
    "FiscalProjectCodePattern": "PRJ{yy}_{yy}{seq5}",
    "SalesDocuments": {
      "AllowPostedInvoices": true,
      "Attachments": { "Enabled": false }
    },
    "Bootstrap": { "UserFields": { "Enabled": true } }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://+:5012" }
    }
  },
  "Jwt": { "Key": "<chiave DIVERSA dalla produzione>", "Issuer": "AX.SAPB1.Api", "Audience": "AX.SAPB1.Client", "ExpiresMinutes": 120 },
  "Auth": { "ApiKeys": [ "<chiave API DIVERSA dalla produzione>" ] }
}
```

- `Kestrel:Endpoints` definisce da solo le porte dell'istanza: niente endpoint `Https` sulla 7226 (è della
  produzione, il secondo servizio non partirebbe), un endpoint `Http` su una porta dedicata (qui 5012). Se serve
  HTTPS si aggiunge un endpoint `Https` su un'altra porta dedicata (es. 7227) con il pfx copiato a mano.
  Partendo da una copia dell'`appsettings.json` di produzione non basta cambiare in `http://` l'URL
  dell'endpoint `Https`: il blocco `Certificate` rimasto fa fallire l'avvio con "The non-HTTPS endpoint Https
  includes HTTPS-only configuration for Certificate" (verificato). Va tolto l'intero endpoint `Https`.
- `Bootstrap:UserFields:Enabled = true` crea all'avvio gli UDF `AX360_InvId/InvNum/DocType` anche su `ORDR`
  nella copia: senza, gli ordini vengono rifiutati (il documento non si crea senza campo di correlazione).
  Sull'istanza di produzione resta spento (chiave assente = spento): i metadati di `SBO_MTF` non si toccano.
- `SalesDocuments:AllowPostedInvoices` abilita le fatture DEFINITIVE (irreversibili). Assente/false = solo bozze
  e ordini: il POST di una fattura definitiva risponde 403.
- `SalesDocuments:Attachments:Enabled` abilita l'upload del PDF in `Attachments2`. È **opt-in** (assente/false =
  spento: il documento nasce senza PDF e lo segnala in `attachmentError`): `true` solo sull'istanza di produzione, o
  su una di test dopo aver dato alla copia una cartella allegati sua. Se SAP rifiuta un documento dopo l'upload, il
  nuovo tentativo riusa la voce già caricata (stesso nome di file) invece di ricaricarla.
- Chiavi `Jwt:Key` e `Auth:ApiKeys` distinte dalla produzione: una chiave di test non deve aprire la produzione.

**Primo impianto** (PowerShell come amministratore sul server; `deploy.ps1` richiede che l'exe esista già):

```powershell
New-Item -ItemType Directory -Force C:\Services\AX.SAPB1.Api.AxTest
# copiare AX.SAPB1.Api.exe (publish win-x64 single-file) e l'appsettings.json dell'istanza
sc.exe create AX.SAPB1.Api.AxTest binPath= "C:\Services\AX.SAPB1.Api.AxTest\AX.SAPB1.Api.exe" start= auto DisplayName= "AX.SAPB1.Api (test SBO_MTF_AXTEST)"
sc.exe start AX.SAPB1.Api.AxTest
```

Log in `C:\Services\AX.SAPB1.Api.AxTest\logs` (separati dalla produzione). All'avvio si vede
`Opening ODBC connection to schema SBO_MTF_AXTEST` e, con il bootstrap acceso, la creazione degli UDF.

**Aggiornamenti:** `deploy.ps1 -ServiceName AX.SAPB1.Api.AxTest -RemoteDir C:\Services\AX.SAPB1.Api.AxTest -Port <porta>`.
Il suo health-check interroga `https://<server>:<porta>/swagger`: con un'istanza solo HTTP passare la porta di
un endpoint `Https` dedicato, oppure verificare a mano (`http://<server>:5012/swagger`).

**Collaudo** (Swagger o `AX.SAPB1.Api.http`, header `X-Api-Key` di test):
1. `GET /api/lookup/items?sellableOnly=true` — articoli di vendita attivi;
2. `POST /api/sales-documents/preview` — payload che verrebbe inviato, avvisi (UdM, UDF assenti), documento esistente;
3. `POST /api/sales-documents` bozza fattura → 201; stesso `correlationId` di nuovo → 200 con `alreadyExisted = true`, nessun doppione;
4. fattura definitiva (con `AllowPostedInvoices`), ordine in bozza e definitivo, allegato (dopo aver sistemato `OADP.AttachPath`);
5. `GET /api/invoices` e `GET /api/sales-orders` — correlazione, stato dell'ordine, fatture tratte (`INV1.BaseType = 17`);
6. `POST /api/fiscal-projects` → codice `PRJ<yy>_<yy><progressivo>` successivo al massimo dell'anno di CREAZIONE (`codeYear` o oggi a Roma, mai l'anno di `validFrom`); la stessa richiesta con lo stesso `idempotencyKey` ripetuta → 200, stesso codice, `created = false`;
7. `GET /api/sales-documents/by-correlation/{id}` → `found`, tipo, stato, `cancelled` del documento creato.
8. timesheet con ore fatturabili: `POST /api/timesheet/lite` con `billableHours` minore di `hours` → in SAP
   `U_TimeNrTot = hours`, `U_TimeNrNet = billableHours`, `U_TimeNrNF` la differenza; `PATCH /api/timesheet/{docEntry}/hours`
   con nuovi valori → 200 `updated`; la stessa richiesta di nuovo → 200 `unchanged`; con `expectedBillableHours` diverso
   da SAP → 409 `changed_in_erp`; su una riga «Fatturato» → 409 `billed`; `GET /api/timesheet/billing-state` riporta
   `hours` (fatturabili) e `totalHours` (lorde, `null` se illeggibili).
   **Ordine di rilascio:** questo servizio va aggiornato PRIMA del portale AX.360 che invia `billableHours`: un
   servizio precedente ignora il campo e scrive `U_TimeNrNet = hours` (vedi `API-ENDPOINTS.md`, "Ordine di
   rilascio"). Prima del rilascio va anche deciso il punto aperto sui campi orari (`U_TimeNF`/`U_TimeEnd`), stessa
   sezione.

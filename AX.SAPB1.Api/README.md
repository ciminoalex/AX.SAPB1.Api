# AX SAP B1 API

API per la gestione dei timesheet tramite connessione ODBC a SAP HANA e SAP Business One Service Layer.

## Descrizione

Questo progetto fornisce un'API REST per:
- **Lettura dati**: Connessione ODBC diretta a SAP HANA per recuperare informazioni sui timesheet
- **Scrittura dati**: Utilizzo del Service Layer di SAP Business One per creare, modificare e cancellare UDO Timesheet

## Struttura del Progetto

```
AX.SAPB1.Api/
├── Controllers/
│   └── TimesheetController.cs          # Controller per le API dei timesheet
├── Models/
│   └── Timesheet.cs                    # Modelli per i dati dei timesheet
├── Services/
│   ├── IHanaOdbcService.cs             # Interfaccia per il servizio ODBC
│   ├── HanaOdbcService.cs              # Implementazione del servizio ODBC
│   ├── ISapB1ServiceLayerService.cs    # Interfaccia per il Service Layer
│   └── SapB1ServiceLayerService.cs     # Implementazione del Service Layer
├── appsettings.json                    # Configurazioni
├── Program.cs                          # Configurazione dell'applicazione
└── README.md                           # Questo file
```

## Configurazione

### 1. Configurazione SAP HANA ODBC

Modifica il file `appsettings.json` con i parametri della tua connessione SAP HANA:

```json
{
  "ConnectionStrings": {
    "SapHana": "Driver={SAP HANA};Server=your-hana-server:30015;Database=your-database;UID=your-username;PWD=your-password;"
  }
}
```

### 2. Configurazione SAP Business One Service Layer

Aggiungi le configurazioni per il Service Layer:

```json
{
  "SapB1": {
    "ServiceLayerUrl": "https://your-b1-server:50000/b1s/v2",
    "CompanyDB": "your-company-db",
    "UserName": "your-username",
    "Password": "your-password"
  }
}
```

## API Endpoints

### Lettura Dati (ODBC SAP HANA)

- `GET /api/timesheet` - Ottiene tutti i timesheet
- `GET /api/timesheet/{docEntry}` - Ottiene un timesheet specifico
- `GET /api/timesheet/employee/{employeeId}` - Ottiene i timesheet per dipendente
- `GET /api/timesheet/project/{projectId}` - Ottiene i timesheet per progetto
- `GET /api/timesheet/daterange?startDate={date}&endDate={date}` - Ottiene i timesheet per intervallo di date

### Scrittura Dati (SAP B1 Service Layer)

- `POST /api/timesheet` - Crea un nuovo timesheet
- `PUT /api/timesheet/{docEntry}` - Aggiorna un timesheet esistente
- `DELETE /api/timesheet/{docEntry}` - Elimina un timesheet

## Modelli Dati

### Timesheet
```json
{
  "docEntry": 123,
  "docNum": "TS001",
  "date": "2024-01-15T00:00:00",
  "employeeId": "EMP001",
  "projectId": "PRJ001",
  "activityId": "ACT001",
  "hours": 8.5,
  "description": "Sviluppo funzionalità",
  "status": "Draft",
  "createdDate": "2024-01-15T10:00:00",
  "createdBy": "admin",
  "modifiedDate": "2024-01-15T11:00:00",
  "modifiedBy": "admin"
}
```

### TimesheetCreateRequest
```json
{
  "date": "2024-01-15T00:00:00",
  "employeeId": "EMP001",
  "projectId": "PRJ001",
  "activityId": "ACT001",
  "hours": 8.5,
  "description": "Sviluppo funzionalità"
}
```

### TimesheetUpdateRequest
```json
{
  "docEntry": 123,
  "date": "2024-01-15T00:00:00",
  "employeeId": "EMP001",
  "projectId": "PRJ001",
  "activityId": "ACT001",
  "hours": 8.5,
  "description": "Sviluppo funzionalità"
}
```

## Installazione e Esecuzione

1. **Prerequisiti**
   - .NET 8.0 SDK
   - Driver ODBC per SAP HANA installato
   - Accesso a SAP Business One Service Layer

2. **Installazione**
   ```bash
   dotnet restore
   ```

3. **Configurazione**
   - Modifica `appsettings.json` con i tuoi parametri di connessione
   - Assicurati che il driver ODBC sia configurato correttamente

4. **Esecuzione**
   ```bash
   dotnet run
   ```

5. **Test**
   - L'API sarà disponibile su `https://localhost:7001` (o porta configurata)
   - Swagger UI disponibile su `https://localhost:7001/swagger`

## Note Importanti

### UDO Timesheet in SAP Business One

L'UDO Timesheet deve essere configurato in SAP Business One con i seguenti campi:
- `U_Date` (Date) - Data del timesheet
- `U_EmployeeId` (Text) - ID del dipendente
- `U_ProjectId` (Text) - ID del progetto
- `U_ActivityId` (Text) - ID dell'attività
- `U_Hours` (Numeric) - Ore lavorate
- `U_Description` (Text) - Descrizione del lavoro
- `U_Status` (Text) - Stato del timesheet

### Sicurezza

- Le password sono memorizzate in chiaro nel file di configurazione per semplicità
- In produzione, utilizzare Azure Key Vault o altri metodi sicuri per gestire le credenziali
- Configurare HTTPS e autenticazione appropriata

### Logging

L'applicazione utilizza il logging strutturato di .NET. I log includono:
- Errori di connessione al database
- Errori nelle operazioni del Service Layer
- Informazioni sulle operazioni API

## Troubleshooting

### Problemi di Connessione ODBC
- Verificare che il driver ODBC sia installato
- Controllare la stringa di connessione
- Verificare le credenziali e i permessi

### Problemi Service Layer
- Verificare l'URL del Service Layer
- Controllare le credenziali di accesso
- Verificare che l'UDO Timesheet sia configurato correttamente

### Errori Comuni
- `Connection string not found`: Verificare la configurazione in `appsettings.json`
- `Failed to login`: Controllare le credenziali del Service Layer
- `UDO not found`: Verificare che l'UDO Timesheet sia configurato in SAP B1

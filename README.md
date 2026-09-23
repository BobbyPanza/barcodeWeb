# BollaImpianto.Web

Applicazione web ASP.NET Core (MVC, .NET 10) per la gestione bolla/impianto e delle liste di
prelievo dei piani di taglio, con analisi di compatibilita' materiale.

Versione corrente: **1.4.0**

## Pagine

| Rotta | Descrizione |
|---|---|
| `/Account/Login` | Login operatore (validazione su `A_OPR`, cookie `BollaImpianto.Auth`) |
| `/Bolla` | Abbinamento bolla ↔ impianto sui piani di lavoro |
| `/ListePrelievo` | Creazione liste di prelievo e assegnazione dei piani (barcode o manuale) |

### Ricerca info bolla / nesting

Nella pagina `/ListePrelievo`, in alto, una casella di ricerca accetta una bolla o un codice
nesting e apre un popup con: macchina assegnata, data prevista di taglio e lista di prelievo
collegata (o l'indicazione che il piano non e' assegnato ad alcuna lista).

Endpoint interno: `GET /ListePrelievo/CercaInfoPiano?q=<bolla|nesting>`.

### Piani assegnati: ripetizioni

La tabella dei piani assegnati alla lista mostra la colonna **Rip.**, il numero di ripetizioni
del piano: somma di `L_NELM.NMRIP` su tutte le righe del nesting.

### Stampa lista ed etichette

Il dialog di stampa espone due scelte indipendenti: una stampante per il report lista e una per
le etichette, ciascuna con il proprio pulsante. Le due scelte vengono memorizzate nei cookie del
dispositivo (`BollaImpianto.StampanteLista`, `BollaImpianto.StampanteEtichette`) e riproposte ai
lanci successivi, cosi' ogni postazione di officina conserva le proprie stampanti.

La stampa etichetta a bordo riga non apre alcun dialog e usa la stampante etichette memorizzata.
Il pulsante *Salva stampanti senza stampare* memorizza le scelte senza lanciare stampe.

## API materiale

Due endpoint per l'analisi di compatibilita' materiale, autenticati con cookie di sessione
**oppure** con API key (vedi *Autenticazione API*).

### Analisi: anomalie, materiale minimo, lamiere disponibili

```
GET  /api/materiale/analisi?bolla=0000234022&soloDisponibili=true
GET  /api/materiale/analisi?idNesting=68698
GET  /api/materiale/analisi?codiceNesting=2026-89-151
POST /api/materiale/analisi     { "lavorazioni": [123, 456], "soloDisponibili": true }
```

Accetta **uno** tra `bolla`, `idNesting`, `codiceNesting`, `lavorazioni` (lista di `A_LAV.IDLAV`).
`soloDisponibili=true` limita alle lamiere con giacenza residua maggiore di zero.

Risposta:

```jsonc
{
  "anomalie": [
    { "code": "MULTIPLE_THICKNESS", "severity": "ERROR",
      "message": "Spessore comune non ricavabile: le parti richiedono spessori diversi.",
      "detail": "Spessori richiesti: 65.000, 70.000" }
  ],
  "materialeMinimo": {
    "idNesting": 68694, "bolla": "0000234022",
    "numLavorazioni": 2, "numParti": 2,
    "spessore": 15.0, "numSpessoriDistinti": 1, "spessoriRichiesti": "15.000",
    "qualitaAmmesse": "CFO, CFS, CFT, CGA",
    "formaX": 1746.0, "formaY": 2429.0,
    "qualita": [ { "code": "CFO", "description": "S355J2+N" } ],
    "attributi": [
      { "columnName": "Alpha3", "jdeCode": "T04VAR03",
        "description": "GRADO QUALITATIVO PROD.STRUTT.",
        "requiredCode": "J2", "requiredWeight": 20000000.0,
        "filterType": "P", "obbligatorio": true }
    ]
  },
  "lamiere": [
    { "trackingId": 130527, "trackingCode": "W26040600",
      "partDescription": "Lamiera Treno S355J2+N 3180x2500x15 1° SCELTA",
      "thickness": 15.0, "qualityCode": "CFO", "compatible": 1,
      "storeCode": "G02", "locationCode": "G02CI", "giacenzaResidua": 14.0,
      "dimX": 3180.0, "dimY": 2500.0, "isGhost": false }
  ],
  "hasError": false
}
```

`materialeMinimo` e' la **specifica minima richiesta**: spessore, qualita' ammesse e, per ogni
attributo, il requisito piu' stringente tra tutte le parti. Non e' una lamiera reale.

`compatible`: `1` = pienamente compatibile, `2` = compatibile con deroghe su attributi opzionali.

Quando `anomalie` contiene una voce con `severity: "ERROR"`, `lamiere` e' vuoto: la ricerca non
viene eseguita perche' i requisiti non sono determinabili in modo affidabile.

### Verifica: una rintracciabilita' e' idonea?

```
GET  /api/materiale/verifica?bolla=0000234022&rintracciabilita=X2170067
POST /api/materiale/verifica    { "idNesting": 68698, "rintracciabilita": "X2170067" }
```

Risposta (oltre a `anomalie` e `materialeMinimo` come sopra):

```jsonc
{
  "validazione": {
    "trackingCode": "X2170067", "thickness": 15.0, "qualityCode": "CEQ",
    "spessoreOk": true, "qualitaOk": false, "attributiOk": false, "dimensioniOk": true,
    "esitoOk": false
  },
  "caratteristicheNonConformi": [
    { "columnName": "Alpha3", "description": "GRADO QUALITATIVO PROD.STRUTT.",
      "valoreLamiera": "JR", "pesoLamiera": 0.0,
      "valoreRichiesto": "J2", "pesoRichiesto": 20000000.0,
      "status": 0, "obbligatorio": true }
  ]
}
```

I quattro controlli (`spessoreOk`, `qualitaOk`, `attributiOk`, `dimensioniOk`) sono valutati in
modo indipendente, cosi' una lamiera scartata indica *quale* requisito non rispetta.
`caratteristicheNonConformi.status`: `0` = non conforme su attributo obbligatorio,
`2` = non conforme su attributo opzionale.

### Anomalie possibili

| Code | Severity | Significato |
|---|---|---|
| `NO_JOBS` | ERROR | Nessuna lavorazione trovata per i parametri indicati |
| `NO_PARTS` | ERROR | Nessuna parte d'ordine collegata alle lavorazioni |
| `MULTIPLE_THICKNESS` | ERROR | Le parti richiedono spessori diversi: materiale comune non ricavabile |
| `NO_THICKNESS` | ERROR | Spessore non ricavabile dalle parti d'ordine |
| `NO_COMMON_QUALITY` | ERROR | Nessuna qualita' compatibile con tutte le parti |
| `NO_SHAPE` | WARNING | Forma non ricavabile: il filtro dimensionale non viene applicato |

## Autenticazione API

Gli endpoint `/api/materiale/*` accettano:

1. il **cookie di sessione** (`BollaImpianto.Auth`), per chiamate dal browser dell'operatore;
2. l'header **`X-Api-Key`**, per sistemi esterni.

Senza credenziali valide la risposta e' `401` con corpo JSON (non un redirect alla pagina di
login, che sarebbe inutilizzabile da un client non browser).

Le chiavi si configurano in `Api:Keys`. Non vanno committate: usare `appsettings.Production.json`
sul server oppure variabili d'ambiente:

```
Api__Keys__0=<chiave>
Api__Keys__1=<seconda chiave>
```

Se `Api:Keys` e' vuoto, l'autenticazione via API key e' disabilitata e resta valido solo il cookie.

## Database

La connessione **non e' committata**: `ConnectionStrings:MyDatabase` in `appsettings.json` e'
vuota di proposito. Valorizzarla in uno di questi modi:

- `appsettings.Production.json` sul server (non committato);
- `appsettings.Development.json` in locale (in `.gitignore`);
- variabile d'ambiente `ConnectionStrings__MyDatabase`.

All'avvio, se la connessione e' assente o vuota, l'applicazione fallisce con un messaggio
esplicito invece di tentare una connessione senza credenziali.

Gli script vanno applicati in ordine di data.

| Script | Contenuto |
|---|---|
| `sql/20260420_lista_clienti_materiale_e_operatore.sql` | Vista clienti/materiale per le liste di prelievo |
| `sql/20260729_material_analysis.sql` | `X_ProductionManager_Material_Analysis` (richiesta dalle API materiale) |

### Come funziona l'analisi materiale

1. **Input → lavorazioni.** Il percorso primario e' `L_PCPA` → `A_LAV` (su `CONUM`, `LOCOD`,
   `IDFAS`; nota che `L_PCPA.IDPTC` = `A_NES.IDNES`). Il percorso via `L_ODLA` e' solo un
   fallback, perche' `L_ODLA` e' popolato unicamente quando il nesting e' stato lanciato in
   produzione: i nesting non lanciati hanno lavorazioni solo via `L_PCPA`.
2. **Lavorazioni → parti d'ordine.** `A_LAV` → `A_LOT` → `L_CMPA` (`LOCOD` = `LOCLM`).
3. **Parti → requisiti.** Spessore da `A_PAR.PADZ1`; qualita' da `L_CMPA.QualityCode` con
   `ComparingMode` (`S` specifica, `R` gruppo, `C` compatibili), intersecate su tutte le parti;
   attributi da `L_CMPA.Alpha1..18` incrociati con `X_JDE_AttributiAnagrafica` /
   `X_JDE_AttributiValori`.
4. **Requisiti → lamiere.** Magazzino da `S_DMP` + `S_CRN` + `L_MLPR` + `A_PAR`, filtrato per
   spessore e qualita', confrontato attributo per attributo e infine filtrato sulle dimensioni
   della forma richiesta (`L_NELM` / `L_PCFO`, con deroga per gli sfridi `CRCOD LIKE 'W%'`).

### Note sulla semantica degli attributi

`X_JDE_AttributiAnagrafica.TipoControllo` vale `P` (confronto per peso: la lamiera deve avere
peso ≥ a quello richiesto) oppure `V` (confronto per valore esatto). Il set dei filtri esclude
`TipoControllo = 'V'`, quindi i quattro attributi di tipo `V` (`Alpha18` zincabilita',
`Alpha19` tolleranza spessore, `Alpha20` codice ferriera, `Alpha21` provenienza ferriera)
**non vengono confrontati**. Analogamente `ControlloAbilitato = 0` (`Alpha9`, `Alpha15`) e'
escluso. Questo comportamento e' ereditato dalle procedure originali ed e' stato mantenuto
invariato: se i controlli di tipo `V` vanno attivati, e' una modifica funzionale da concordare.

Il confronto sulla scheda cliente non e' ancora implementato.

### Relazione con le procedure preesistenti

`X_ProductionManager_Material_Analysis` sostituisce l'uso diretto di
`X_ProductionManager_Compatible_Sheets` e `X_ProductionManager_Compatible_Sheets_List`, che
restano invariate sul database. L'elenco lamiere prodotto e' stato verificato identico a quello
della procedura originale (confronto `EXCEPT` in entrambe le direzioni: zero differenze).

Ottimizzazioni, misurate sul dataset di test (~4200 lamiere candidate):

| | Originale | Nuova |
|---|---|---|
| Analisi completa | ~2100 ms | ~330 ms |
| Verifica singola rintracciabilita' | n/d | ~70 ms |

Il guadagno principale viene dall'aver spostato `X_ProductionManager_Job_Thickness()` e
`X_ProductionManager_Qualities()` fuori dalla `WHERE`: chiamate inline impedivano un piano
efficiente e quello step, da solo, passa da ~1500 ms a ~65 ms. Contribuiscono inoltre
l'accorpamento delle temp table intermedie di confronto e il calcolo della forma richiesta una
volta sola invece che in `OUTER APPLY` per riga.

## Build e pubblicazione

```
dotnet build BollaImpianto.Web/BollaImpianto.Web.csproj
dotnet publish BollaImpianto.Web/BollaImpianto.Web.csproj -c Release -o publish/BollaImpianto.Web
```

## Configurazione

| Chiave | Descrizione |
|---|---|
| `ConnectionStrings:MyDatabase` | Connessione SQL Server. Vuota in `appsettings.json`: va fornita fuori dal repo |
| `Api:Keys` | Chiavi per l'autenticazione API (array). Vuoto = solo cookie |
| `PrinterManager:IndirizzoPrinterManager` | Endpoint del servizio di stampa |
| `PrinterManager:NomeReportLista` | Report per la stampa della lista |
| `PrinterManager:NomeReportEtichetta` | Report per le etichette della lista |
| `PrinterManager:NomeReportEtichettaSingola` | Report per l'etichetta di un singolo nesting |

La sessione usa cookie con `SlidingExpiration` e scadenza 10 ore. Su IIS, se le chiavi di Data
Protection non sono persistite, il riciclo dell'application pool invalida tutti i cookie emessi
e disconnette simultaneamente gli utenti collegati.

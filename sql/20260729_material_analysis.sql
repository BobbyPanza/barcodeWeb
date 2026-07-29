/*
    Analisi materiale + validazione rintracciabilita'.

    Sostituisce l'uso diretto di X_ProductionManager_Compatible_Sheets /
    X_ProductionManager_Compatible_Sheets_List con una versione unica e ottimizzata.

    Ottimizzazioni rispetto alle SP originali:
      - spessore e qualita' precalcolati in variabile/temp table invece di essere
        richiamati dentro la WHERE (era il collo di bottiglia: ~1500ms -> ~65ms)
      - forma richiesta calcolata una volta sola invece che in OUTER APPLY per riga
      - le tre temp table intermedie di confronto accorpate in una sola aggregazione

    Input: uno tra bolla / IDNES / codice nesting / lista IDLAV.
    Il percorso primario e' L_PCPA -> A_LAV perche' L_ODLA e' popolato solo
    quando il nesting e' stato lanciato in produzione.
*/

IF OBJECT_ID('dbo.X_ProductionManager_Material_Analysis', 'P') IS NOT NULL
    DROP PROCEDURE dbo.X_ProductionManager_Material_Analysis;
GO

CREATE PROCEDURE dbo.X_ProductionManager_Material_Analysis
    @JobTicketCode  varchar(20)   = NULL,  -- bolla di lavoro (A_NES.OLCOD)
    @NestingId      int           = NULL,  -- A_NES.IDNES
    @NestingCode    varchar(50)   = NULL,  -- A_NES.NSCOD
    @TrackingCode   varchar(50)   = NULL,  -- rintracciabilita' da validare (S_CRN.CRCOD)
    @SoloDisponibili bit          = 0,     -- 1 = solo lamiere con giacenza residua > 0
    @Jobs           JobOrderTable READONLY -- lista lavorazioni (A_LAV.IDLAV)
AS
BEGIN
    SET NOCOUNT ON;

    DROP TABLE IF EXISTS #anomalie;
    CREATE TABLE #anomalie (
        Code     varchar(40)  NOT NULL,
        Severity varchar(10)  NOT NULL,   -- ERROR blocca l'esito, WARNING no
        Message  varchar(400) NOT NULL,
        Detail   varchar(400) NULL
    );

    /* ---------- 1. risoluzione input -> lavorazioni (IDLAV) ---------- */

    DROP TABLE IF EXISTS #jobs;
    CREATE TABLE #jobs (IDLAV int NOT NULL PRIMARY KEY);

    DECLARE @resolvedNestingId int = @NestingId;

    IF @resolvedNestingId IS NULL AND @NestingCode IS NOT NULL
        SELECT TOP (1) @resolvedNestingId = IDNES
        FROM dbo.A_NES
        WHERE NSCOD = @NestingCode
        ORDER BY IDNES DESC;

    IF @resolvedNestingId IS NULL AND @JobTicketCode IS NOT NULL
        SELECT TOP (1) @resolvedNestingId = IDNES
        FROM dbo.A_NES
        WHERE OLCOD = @JobTicketCode
        ORDER BY IDNES DESC;

    IF EXISTS (SELECT 1 FROM @Jobs)
    BEGIN
        INSERT INTO #jobs (IDLAV)
        SELECT DISTINCT JobOrderID FROM @Jobs WHERE JobOrderID IS NOT NULL;
    END
    ELSE IF @resolvedNestingId IS NOT NULL
    BEGIN
        INSERT INTO #jobs (IDLAV)
        SELECT DISTINCT lav.IDLAV
        FROM dbo.L_PCPA pa
        INNER JOIN dbo.A_LAV lav
            ON lav.CONUM = pa.CONUM AND lav.LOCOD = pa.LOCOD AND lav.IDFAS = pa.IDFAS
        WHERE pa.IDPTC = @resolvedNestingId AND lav.IDLAV IS NOT NULL;
    END
    ELSE IF @JobTicketCode IS NOT NULL
    BEGIN
        -- fallback: bolla lanciata in produzione ma nesting non risolto
        INSERT INTO #jobs (IDLAV)
        SELECT DISTINCT odla.IDLAV
        FROM dbo.L_ODLA odla
        WHERE odla.OLCOD = @JobTicketCode AND odla.IDLAV IS NOT NULL;
    END

    IF NOT EXISTS (SELECT 1 FROM #jobs)
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('NO_JOBS', 'ERROR', 'Nessuna lavorazione trovata per i parametri indicati.',
                'Verificare bolla / nesting: il nesting potrebbe non avere lavorazioni collegate.');

    /* ---------- 2. lavorazioni -> parti d'ordine (L_CMPA.IDROW) ---------- */

    DROP TABLE IF EXISTS #parts;
    CREATE TABLE #parts (OrderPartId int NOT NULL PRIMARY KEY);

    INSERT INTO #parts (OrderPartId)
    SELECT DISTINCT cmpa.IDROW
    FROM #jobs j
    INNER JOIN dbo.A_LAV lav ON lav.IDLAV = j.IDLAV
    INNER JOIN dbo.A_LOT lot ON lav.CONUM = lot.CONUM AND lav.LOCOD = lot.LOCOD
    INNER JOIN dbo.L_CMPA cmpa ON cmpa.CONUM = lot.CONUM AND cmpa.LOCOD = lot.LOCLM;

    IF NOT EXISTS (SELECT 1 FROM #parts) AND EXISTS (SELECT 1 FROM #jobs)
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('NO_PARTS', 'ERROR', 'Nessuna parte d''ordine collegata alle lavorazioni.', NULL);

    /* ---------- 3. spessore richiesto ---------- */

    DROP TABLE IF EXISTS #thickness;
    SELECT DISTINCT par.PADZ1 AS Thickness
    INTO #thickness
    FROM #parts p
    INNER JOIN dbo.L_CMPA cmpa ON cmpa.IDROW = p.OrderPartId
    INNER JOIN dbo.A_PAR par ON cmpa.PACOD = par.PACOD
    WHERE par.PADZ1 IS NOT NULL;

    DECLARE @numThickness int, @thicknessList varchar(400), @thickness float;
    SELECT @numThickness = COUNT(*) FROM #thickness;
    SELECT @thicknessList = STUFF((SELECT ', ' + CONVERT(varchar(30), CAST(Thickness AS decimal(18,3)))
                                   FROM #thickness ORDER BY Thickness
                                   FOR XML PATH('')), 1, 2, '');

    IF @numThickness > 1
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('MULTIPLE_THICKNESS', 'ERROR',
                'Spessore comune non ricavabile: le parti richiedono spessori diversi.',
                'Spessori richiesti: ' + ISNULL(@thicknessList, ''));
    ELSE IF @numThickness = 0 AND EXISTS (SELECT 1 FROM #parts)
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('NO_THICKNESS', 'ERROR', 'Spessore non ricavabile dalle parti d''ordine.', NULL);
    ELSE
        SELECT @thickness = Thickness FROM #thickness;

    /* ---------- 4. qualita' ammesse ---------- */

    DROP TABLE IF EXISTS #qual;
    CREATE TABLE #qual (Code varchar(10) NOT NULL PRIMARY KEY, Description varchar(30) NULL);

    ;WITH PartQualities AS (
        SELECT cmpa.IDROW AS OrderPartId, cmpa.QualityCode, cmpa.ComparingMode
        FROM #parts p
        INNER JOIN dbo.L_CMPA cmpa ON cmpa.IDROW = p.OrderPartId
        WHERE cmpa.ComparingMode IS NOT NULL
    ),
    Specific AS (
        SELECT DISTINCT pq.OrderPartId, qua.Code, qua.Description
        FROM PartQualities pq
        INNER JOIN dbo.JDEQuality qua ON pq.QualityCode = qua.Code
        WHERE pq.ComparingMode = 'S'
    ),
    Grouped AS (
        SELECT DISTINCT pq.OrderPartId, qua.Code, qua.Description
        FROM PartQualities pq
        INNER JOIN dbo.JDEQuality qua
            ON (SELECT [GROUPING] FROM dbo.JDEQuality src WHERE src.Code = pq.QualityCode) = qua.[GROUPING]
        WHERE pq.ComparingMode = 'R'
    ),
    Compatible AS (
        SELECT pq.OrderPartId, com.Compatible AS Code, qua.Description
        FROM PartQualities pq
        INNER JOIN dbo.JDECompatibility com ON pq.QualityCode = com.Code
        INNER JOIN dbo.JDEQuality qua ON com.Compatible = qua.Code
        WHERE pq.ComparingMode = 'C'
        UNION
        SELECT pq.OrderPartId, qua.Code, qua.Description
        FROM PartQualities pq
        INNER JOIN dbo.JDEQuality qua ON pq.QualityCode = qua.Code
        WHERE pq.ComparingMode = 'C'
    ),
    AllQual AS (
        SELECT * FROM Specific
        UNION ALL SELECT * FROM Grouped
        UNION ALL SELECT * FROM Compatible
    )
    INSERT INTO #qual (Code, Description)
    SELECT Code, MAX(Description)
    FROM AllQual
    GROUP BY Code
    -- una qualita' e' ammessa solo se compatibile con TUTTE le parti
    HAVING COUNT(DISTINCT OrderPartId) = (SELECT COUNT(DISTINCT OrderPartId) FROM PartQualities);

    IF NOT EXISTS (SELECT 1 FROM #qual) AND EXISTS (SELECT 1 FROM #parts)
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('NO_COMMON_QUALITY', 'ERROR',
                'Nessuna qualita'' materiale compatibile con tutte le parti.',
                'Le parti richiedono qualita'' tra loro incompatibili.');

    /* ---------- 5. attributi richiesti (materiale minimo) ---------- */

    DROP TABLE IF EXISTS #attr;
    SELECT
        AttributeColumn AS FilterColumnName,
        AttributeCode   AS FilterCode,
        MAX(val.Peso)   AS FilterWeight,
        anag.TipoControllo      AS FilterType,
        anag.ControlloAbilitato AS FilterCheckType,
        MAX(anag.CodiceJDE)     AS JDECode,
        MAX(anag.Descrizione)   AS Description
    INTO #attr
    FROM (
        SELECT Alpha1,Alpha2,Alpha3,Alpha4,Alpha5,Alpha6,Alpha7,Alpha8,Alpha9,
               Alpha10,Alpha11,Alpha12,Alpha13,Alpha14,Alpha15,Alpha16,Alpha17,Alpha18
        FROM dbo.L_CMPA cmpa
        WHERE EXISTS (SELECT 1 FROM #parts p WHERE p.OrderPartId = cmpa.IDROW)
    ) AttributeCodes
    UNPIVOT (AttributeCode FOR AttributeColumn IN (
        Alpha1,Alpha2,Alpha3,Alpha4,Alpha5,Alpha6,Alpha7,Alpha8,Alpha9,
        Alpha10,Alpha11,Alpha12,Alpha13,Alpha14,Alpha15,Alpha16,Alpha17,Alpha18)) AttributeColumn
    INNER JOIN dbo.X_JDE_AttributiAnagrafica anag ON AttributeColumn = anag.CodiceFactory
    INNER JOIN dbo.X_JDE_AttributiValori val
        ON val.ID_X_JDE_AttributiAnagrafica = anag.ID AND val.Codice = AttributeCode
    WHERE ISNULL(AttributeCode, '') <> ''
      AND anag.TipoControllo <> 'V'
      AND anag.ControlloAbilitato IN (1, 2)
    GROUP BY AttributeColumn, AttributeCode, anag.TipoControllo, anag.ControlloAbilitato;

    CREATE CLUSTERED INDEX ix_attr ON #attr(FilterColumnName);

    /* ---------- 6. forma richiesta (una volta sola) ---------- */

    DECLARE @foX float, @foY float;
    SELECT @foX = MAX(CASE WHEN ISNULL(nelm.PCMNX, 0) = 0 THEN fo.PFDX1 ELSE nelm.PCMNX END),
           @foY = MAX(CASE WHEN ISNULL(nelm.PCMNY, 0) = 0 THEN fo.PFDY1 ELSE nelm.PCMNY END)
    FROM dbo.L_NELM nelm
    INNER JOIN dbo.L_PCFO fo ON nelm.IDFOR = fo.IDFOR AND nelm.IDNES = fo.IDPTC
    WHERE nelm.IDNES = @resolvedNestingId;

    IF @foX IS NULL AND @TrackingCode IS NULL
        INSERT INTO #anomalie (Code, Severity, Message, Detail)
        VALUES ('NO_SHAPE', 'WARNING', 'Forma richiesta non ricavabile: il filtro dimensionale non viene applicato.',
                'Le lamiere sono valutate solo su spessore, qualita'' e attributi.');

    DECLARE @hasError bit = CASE WHEN EXISTS (SELECT 1 FROM #anomalie WHERE Severity = 'ERROR') THEN 1 ELSE 0 END;

    /* ---------- 7. lamiere candidate + confronto attributi ---------- */

    DROP TABLE IF EXISTS #sheetAttr;
    CREATE TABLE #sheetAttr (
        TrackingId   int NOT NULL,
        TrackingCode varchar(50) NOT NULL,
        ColumnName   varchar(20) NOT NULL,
        Code         varchar(50) NULL,
        JDECode      varchar(50) NULL,
        Description  varchar(200) NULL,
        Weight       numeric(18,6) NULL
    );

    IF @hasError = 0
    BEGIN
        INSERT INTO #sheetAttr (TrackingId, TrackingCode, ColumnName, Code, JDECode, Description, Weight)
        SELECT TrackingId, TrackingCode, AttributeColumn, AttributeCode,
               anag.CodiceJDE, anag.Descrizione, val.Peso
        FROM (
            SELECT crn.IDCRN AS TrackingId, crn.CRCOD AS TrackingCode,
                   dmp.Alpha1,dmp.Alpha2,dmp.Alpha3,dmp.Alpha4,dmp.Alpha5,dmp.Alpha6,dmp.Alpha7,
                   dmp.Alpha8,dmp.Alpha9,dmp.Alpha10,dmp.Alpha11,dmp.Alpha12,dmp.Alpha13,
                   dmp.Alpha14,dmp.Alpha15,dmp.Alpha16,dmp.Alpha17,dmp.Alpha18
            FROM dbo.S_DMP dmp
            INNER JOIN dbo.S_CRN crn ON dmp.IDDMP = crn.IDDMP
            INNER JOIN dbo.L_MLPR mlpr ON mlpr.IDCRN = crn.IDCRN
            INNER JOIN dbo.A_PAR par ON mlpr.PACOD = par.PACOD
            WHERE (@TrackingCode IS NOT NULL OR par.PADZ1 = @thickness)
              AND (@TrackingCode IS NOT NULL OR EXISTS (SELECT 1 FROM #qual q WHERE q.Code = par.x_JDEQuality))
              AND (@TrackingCode IS NULL OR crn.CRCOD = @TrackingCode)
              AND (@SoloDisponibili = 0 OR mlpr.CRQTR > 0)
        ) AS Trackings
        UNPIVOT (AttributeCode FOR AttributeColumn IN (
            Alpha1,Alpha2,Alpha3,Alpha4,Alpha5,Alpha6,Alpha7,Alpha8,Alpha9,
            Alpha10,Alpha11,Alpha12,Alpha13,Alpha14,Alpha15,Alpha16,Alpha17,Alpha18)) AttributeColumn
        INNER JOIN dbo.X_JDE_AttributiAnagrafica anag ON AttributeColumn = anag.CodiceFactory
        INNER JOIN dbo.X_JDE_AttributiValori val
            ON val.ID_X_JDE_AttributiAnagrafica = anag.ID AND val.Codice = AttributeCode
        WHERE ISNULL(AttributeCode, '') <> ''
        -- RECOMPILE: rende sargabili i guard su @TrackingCode/@thickness, che altrimenti
        -- forzano una scansione dell'intero magazzino
        OPTION (RECOMPILE);

        CREATE CLUSTERED INDEX ix_sa ON #sheetAttr(TrackingId, ColumnName);
    END

    -- esito per singolo attributo: 1 ok, 2 non conforme ma opzionale, 3 non richiesto, 0 non conforme
    DROP TABLE IF EXISTS #attrCheck;
    SELECT s.TrackingId, s.TrackingCode, s.ColumnName,
           s.JDECode, s.Description, s.Code AS SheetCode, s.Weight AS SheetWeight,
           a.FilterCode AS RequiredCode, a.FilterWeight AS RequiredWeight,
           a.FilterType, a.FilterCheckType,
           CASE
               WHEN a.FilterType = 'V' THEN
                    CASE WHEN s.Code = a.FilterCode THEN 1
                         ELSE CASE WHEN a.FilterCheckType = 1 THEN 0 WHEN a.FilterCheckType = 2 THEN 2 END END
               WHEN a.FilterType = 'P' THEN
                    CASE WHEN s.Weight >= a.FilterWeight THEN 1
                         ELSE CASE WHEN a.FilterCheckType = 1 THEN 0 WHEN a.FilterCheckType = 2 THEN 2 END END
               WHEN a.FilterType IS NULL THEN 3
           END AS Status
    INTO #attrCheck
    FROM #sheetAttr s
    LEFT JOIN #attr a ON s.ColumnName = a.FilterColumnName;

    DROP TABLE IF EXISTS #compat;
    SELECT TrackingId, TrackingCode,
           CASE WHEN SUM(CASE WHEN Status IN (1,3) THEN 1 ELSE 0 END) = COUNT(*) THEN 1
                WHEN SUM(CASE WHEN Status IN (1,2,3) THEN 1 ELSE 0 END) = COUNT(*) THEN 2
                ELSE 0 END AS Compatible
    INTO #compat
    FROM #attrCheck
    GROUP BY TrackingId, TrackingCode;

    /* ---------- RESULT SET 1: anomalie ---------- */
    SELECT Code, Severity, Message, Detail FROM #anomalie ORDER BY CASE Severity WHEN 'ERROR' THEN 0 ELSE 1 END, Code;

    /* ---------- RESULT SET 2: materiale minimo (sintesi) ---------- */
    DECLARE @qualList varchar(1000);
    SELECT @qualList = STUFF((SELECT ', ' + Code FROM #qual ORDER BY Code FOR XML PATH('')), 1, 2, '');

    SELECT
        @resolvedNestingId AS NestingId,
        @JobTicketCode     AS JobTicketCode,
        (SELECT COUNT(*) FROM #jobs)  AS NumLavorazioni,
        (SELECT COUNT(*) FROM #parts) AS NumParti,
        @thickness        AS Spessore,
        @numThickness     AS NumSpessoriDistinti,
        @thicknessList    AS SpessoriRichiesti,
        @qualList         AS QualitaAmmesse,
        @foX              AS FormaX,
        @foY              AS FormaY,
        @hasError         AS HasError;

    /* ---------- RESULT SET 3: qualita' ammesse ---------- */
    SELECT Code, Description FROM #qual ORDER BY Code;

    /* ---------- RESULT SET 4: attributi richiesti (specifica minima) ---------- */
    SELECT FilterColumnName AS ColumnName, JDECode, Description,
           FilterCode AS RequiredCode, FilterWeight AS RequiredWeight,
           FilterType, FilterCheckType,
           CASE WHEN FilterCheckType = 1 THEN 1 ELSE 0 END AS Obbligatorio
    FROM #attr
    ORDER BY FilterColumnName;

    /* ---------- RESULT SET 5: lamiere ---------- */
    IF @TrackingCode IS NULL
    BEGIN
        SELECT DISTINCT c.TrackingId, c.TrackingCode,
               par.PACOD AS PartCode, par.PADSC AS PartDescription, par.PADZ1 AS Thickness,
               mlpr.MGCOD AS StoreCode, mlpr.LCCOD AS LocationCode,
               mlpr.CRQTR AS WarehouseResidualQuantity,
               par.x_JDEQuality AS QualityCode, c.Compatible,
               CAST(drcr.Numeric1 AS float) AS DimX, CAST(drcr.Numeric2 AS float) AS DimY,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.X_Ghost_details gd
                                 INNER JOIN dbo.X_ghost g ON g.id = gd.ghostId
                                 WHERE g.cutPerformed = 0 AND gd.trackingId = mlpr.idmlpr)
                    THEN 1 ELSE 0 END AS IsGhost
        FROM #compat c
        INNER JOIN dbo.S_CRN crn ON crn.IDCRN = c.TrackingId
        INNER JOIN dbo.L_MLPR mlpr ON mlpr.IDCRN = crn.IDCRN
        INNER JOIN dbo.A_PAR par ON mlpr.PACOD = par.PACOD
        LEFT JOIN dbo.S_DMP drcr ON drcr.IDDMP = crn.IDDMP
        WHERE c.Compatible IN (1, 2)
          AND ((@foX IS NULL
                OR (CAST(drcr.Numeric1 AS float) > @foX * 0.96 AND CAST(drcr.Numeric2 AS float) > @foY * 0.96))
               OR crn.CRCOD LIKE 'W%')
        ORDER BY c.Compatible, mlpr.CRQTR DESC;
    END
    ELSE
    BEGIN
        -- validazione della singola rintracciabilita': spessore, qualita', attributi, dimensioni
        SELECT DISTINCT
               crn.IDCRN AS TrackingId, crn.CRCOD AS TrackingCode,
               par.PACOD AS PartCode, par.PADSC AS PartDescription, par.PADZ1 AS Thickness,
               mlpr.MGCOD AS StoreCode, mlpr.LCCOD AS LocationCode,
               mlpr.CRQTR AS WarehouseResidualQuantity,
               par.x_JDEQuality AS QualityCode,
               ISNULL(c.Compatible, 0) AS Compatible,
               CAST(drcr.Numeric1 AS float) AS DimX, CAST(drcr.Numeric2 AS float) AS DimY,
               CASE WHEN par.PADZ1 = @thickness THEN 1 ELSE 0 END AS SpessoreOk,
               CASE WHEN EXISTS (SELECT 1 FROM #qual q WHERE q.Code = par.x_JDEQuality) THEN 1 ELSE 0 END AS QualitaOk,
               CASE WHEN ISNULL(c.Compatible, 0) IN (1, 2) THEN 1 ELSE 0 END AS AttributiOk,
               CASE WHEN @foX IS NULL OR crn.CRCOD LIKE 'W%'
                         OR (CAST(drcr.Numeric1 AS float) > @foX * 0.96 AND CAST(drcr.Numeric2 AS float) > @foY * 0.96)
                    THEN 1 ELSE 0 END AS DimensioniOk
        FROM dbo.S_CRN crn
        INNER JOIN dbo.L_MLPR mlpr ON mlpr.IDCRN = crn.IDCRN
        INNER JOIN dbo.A_PAR par ON mlpr.PACOD = par.PACOD
        LEFT JOIN dbo.S_DMP drcr ON drcr.IDDMP = crn.IDDMP
        LEFT JOIN #compat c ON c.TrackingId = crn.IDCRN
        WHERE crn.CRCOD = @TrackingCode;
    END

    /* ---------- RESULT SET 6: dettaglio attributi non conformi ---------- */
    IF @TrackingCode IS NULL
        SELECT TOP (0) CAST(NULL AS varchar(20)) AS ColumnName, CAST(NULL AS varchar(50)) AS JDECode,
               CAST(NULL AS varchar(200)) AS Description, CAST(NULL AS varchar(50)) AS SheetCode,
               CAST(NULL AS numeric(18,6)) AS SheetWeight, CAST(NULL AS varchar(50)) AS RequiredCode,
               CAST(NULL AS numeric(18,6)) AS RequiredWeight, CAST(NULL AS int) AS Status,
               CAST(NULL AS int) AS Obbligatorio;
    ELSE
        SELECT ColumnName, JDECode, Description, SheetCode, SheetWeight,
               RequiredCode, RequiredWeight, Status,
               CASE WHEN FilterCheckType = 1 THEN 1 ELSE 0 END AS Obbligatorio
        FROM #attrCheck
        WHERE Status IN (0, 2)
        ORDER BY Status, ColumnName;
END
GO

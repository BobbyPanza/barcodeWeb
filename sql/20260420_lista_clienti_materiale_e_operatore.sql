    USE [INTESI-Factory];
    GO

    /* 1) Vista minimale: solo Clienti + Materiale per lista */
    CREATE OR ALTER VIEW dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE
    AS
    SELECT
        ldp.ID AS IDLista,
        ldpr.IDNesting AS IDNesting,
        nes.OLCOD AS Bolla,
        nes.NSCOD AS CodiceNesting,
        ISNULL(tmt.TMDSC, '') AS Materiale,
    CONVERT(int, ROUND(fo.PFDX1, 0)) AS Lunghezza,
    CONVERT(int, ROUND(fo.PFDY1, 0)) AS Larghezza,
    CONVERT(int, ROUND(fo.PFDZ1, 0)) AS Spessore,
        STUFF(
            (
                SELECT DISTINCT CHAR(10) + t1.CTDSC
                FROM dbo.A_COM t1
                INNER JOIN dbo.L_CMPA t2 ON t1.conum = t2.conum
                INNER JOIN dbo.A_LOT t3 ON t3.conum = t2.conum AND t3.loclm = t2.LOCOD
                INNER JOIN dbo.L_PCPA t4 ON t4.conum = t3.conum AND t4.locod = t3.locod
                WHERE t4.IDPTC = nes.IDNES
                FOR XML PATH(''), TYPE
            ).value('.', 'varchar(max)'),
            1, 1, ''
        ) AS Clienti
    FROM dbo.XT_LISTA_DI_PRELIEVO ldp
    INNER JOIN dbo.XT_LISTA_DI_PRELIEVO_RIGHE ldpr ON ldpr.IDLista = ldp.ID
    INNER JOIN dbo.A_NES nes ON nes.IDNES = ldpr.IDNesting
    LEFT JOIN dbo.L_PCFO fo ON fo.IDPTC = nes.IDNES
    LEFT JOIN dbo.A_TMT tmt ON tmt.tmcod = fo.mtcod;
    GO

    /* 2) Campo operatore assegnazione impianto su A_NES */
    IF COL_LENGTH('dbo.A_NES', 'x_Op_Ass') IS NULL
    BEGIN
        ALTER TABLE dbo.A_NES
        ADD x_Op_Ass VARCHAR(50) NULL;
    END
    GO

    /* 3) (Opzionale ma consigliato) indice per filtro "solo assegnazioni mie" */
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_A_NES_x_Op_Ass'
        AND object_id = OBJECT_ID('dbo.A_NES')
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_A_NES_x_Op_Ass
        ON dbo.A_NES (x_Op_Ass)
        INCLUDE (IDNES, OLCOD, MACOD, DTEXP);
    END
    GO

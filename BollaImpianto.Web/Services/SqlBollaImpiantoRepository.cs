using System.Globalization;
using BollaImpianto.Web.Models;
using Microsoft.Data.SqlClient;

namespace BollaImpianto.Web.Services;

public sealed class SqlBollaImpiantoRepository(IConfiguration configuration) : IBollaImpiantoRepository
{
    private const string MacchinaGenericaPlasma = "DLG-ALL_PLASMA";

    private readonly string _connectionString =
        configuration.GetConnectionString("MyDatabase") is { } cs && !string.IsNullOrWhiteSpace(cs)
            ? cs
            : throw new InvalidOperationException(
                "Connection string 'MyDatabase' non configurata. Impostarla in appsettings.Production.json "
                + "oppure nella variabile d'ambiente ConnectionStrings__MyDatabase.");

    public async Task<OperatoreAuth?> ValidateOperatoreAsync(string opCod, string password, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (1) OPCOD, OPDSC
            FROM dbo.A_OPR
            WHERE OPCOD = @OpCod AND LTRIM(RTRIM(ISNULL(OPPSW, ''))) = @Password
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@OpCod", opCod.Trim());
        command.Parameters.AddWithValue("@Password", (password ?? string.Empty).Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatoreAuth
        {
            Codice = ReadNullableString(reader, 0) ?? string.Empty,
            Descrizione = ReadNullableString(reader, 1) ?? string.Empty
        };
    }

    public async Task<IReadOnlyList<PianoLavoroRow>> GetPianiAsync(string? search, bool onlyMine, string? operatore, CancellationToken cancellationToken)
    {
        var rows = new List<PianoLavoroRow>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var hasOperatoreColumn = await ColumnExistsAsync(connection, "XV_A_NES_BOLLAIMPIANTO", "x_Op_Ass", cancellationToken);
        var applyMineFilter = onlyMine && hasOperatoreColumn;
        var hasOlCodOnView = await ColumnExistsAsync(connection, "XV_A_NES_BOLLAIMPIANTO", "OLCOD", cancellationToken);
        var hasPlacche = await ColumnExistsAsync(connection, "WorkplanJobsSheets", "WorkplanID", cancellationToken);

        var bollaExpr = hasOlCodOnView
            ? "LTRIM(RTRIM(ISNULL(src.OLCOD, ''))) as Bolla"
            : "LTRIM(RTRIM(ISNULL(nes.OLCOD, ''))) as Bolla";
        var bollaJoin = hasOlCodOnView
            ? ""
            : "\nINNER JOIN dbo.A_NES nes ON nes.IDNES = src.IDNES";
        var bollaSearchExpr = hasOlCodOnView
            ? "OR LTRIM(RTRIM(ISNULL(src.OLCOD, ''))) LIKE '%' + @Search + '%'"
            : "OR LTRIM(RTRIM(ISNULL(nes.OLCOD, ''))) LIKE '%' + @Search + '%'";
        var placcheSelect = hasPlacche
            ? ", plc.Placche"
            : ", CAST(NULL as varchar(max)) as Placche";
        var placcheApply = hasPlacche
            ? "\nOUTER APPLY (\n    SELECT STUFF(\n        (\n            SELECT DISTINCT CHAR(10) + t1.CRCOD\n            FROM dbo.S_CRN t1\n            INNER JOIN dbo.L_MLPR t2 ON t1.IDCRN = t2.IDCRN\n            INNER JOIN dbo.WorkplanJobsSheets t3 ON t3.StoreLocationPartTrackingID = t2.IDMLPR\n            WHERE t3.WorkplanID = src.IDNES\n            FOR XML PATH(''), TYPE\n        ).value('.', 'varchar(max)'), 1, 1, ''\n    ) AS Placche\n) plc"
            : "";

        var query = $"""
            SELECT src.IDNES, {bollaExpr}, src.NSDSC, src.MADSC, src.DTEXP, src.NSNOT, src.TEMPO, src.RIPET{placcheSelect}
            FROM XV_A_NES_BOLLAIMPIANTO src{bollaJoin}{placcheApply}
            WHERE (@Search IS NULL OR src.NSDSC LIKE '%' + @Search + '%' OR src.MADSC LIKE '%' + @Search + '%' {bollaSearchExpr})
              AND (@OnlyMine = 0 OR ISNULL(src.x_Op_Ass, '') = ISNULL(@Operatore, ''))
            ORDER BY src.IDNES DESC
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
        command.Parameters.AddWithValue("@OnlyMine", applyMineFilter ? 1 : 0);
        command.Parameters.AddWithValue("@Operatore", string.IsNullOrWhiteSpace(operatore) ? DBNull.Value : operatore.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PianoLavoroRow
            {
                IdNes = reader.GetInt32(0),
                Bolla = ReadNullableString(reader, 1) ?? string.Empty,
                NsDsc = ReadNullableString(reader, 2) ?? string.Empty,
                MaDsc = ReadNullableString(reader, 3) ?? string.Empty,
                DtExp = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                NsNot = ReadNullableString(reader, 5),
                Tempo = ReadNullableDecimal(reader, 6),
                Ripet = ReadNullableInt(reader, 7),
                Placche = ReadNullableString(reader, 8)
            });
        }

        return rows;
    }

    public async Task<string> ExecuteBollaImpiantoAsync(string bolla, string impianto, string operatore, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("dbo.XSP_BOLLA_IMPIANTO", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@bolla", bolla.Trim());
        command.Parameters.AddWithValue("@impianto", impianto.Trim());
        var result = await command.ExecuteScalarAsync(cancellationToken);

        var hasOperatoreColumn = await ColumnExistsAsync(connection, "A_NES", "x_Op_Ass", cancellationToken);
        if (!string.IsNullOrWhiteSpace(operatore) && hasOperatoreColumn)
        {
            const string updateOperatoreQuery = """
                UPDATE dbo.A_NES
                SET x_Op_Ass = @Operatore
                WHERE OLCOD = @Bolla
                  AND (MACOD = @Impianto OR @Impianto IS NULL)
                """;
            await using var updateCommand = new SqlCommand(updateOperatoreQuery, connection);
            updateCommand.Parameters.AddWithValue("@Operatore", operatore.Trim());
            updateCommand.Parameters.AddWithValue("@Bolla", bolla.Trim());
            updateCommand.Parameters.AddWithValue("@Impianto", string.IsNullOrWhiteSpace(impianto) ? DBNull.Value : impianto.Trim());
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return result?.ToString() ?? "Operazione completata senza messaggio.";
    }

    public async Task UpdatePianoLavoroAsync(int idNes, DateTime? dataAssegnazione, string? note, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            UPDATE dbo.A_NES
            SET DTEXP = @DataAssegnazione, NSNOT = @Note
            WHERE IDNES = @IdNes
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdNes", idNes);
        command.Parameters.AddWithValue("@DataAssegnazione", (object?)dataAssegnazione ?? DBNull.Value);
        command.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetMacchinaGenericaPlasmaAsync(int idNes, CancellationToken cancellationToken)
    {
        if (idNes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idNes));
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            UPDATE dbo.A_NES
            SET MACOD = @MacCod
            WHERE IDNES = @IdNes
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdNes", idNes);
        command.Parameters.AddWithValue("@MacCod", MacchinaGenericaPlasma);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ListaPrelievoHeader>> GetListePrelievoAsync(bool hideCompleted, CancellationToken cancellationToken)
    {
        var rows = new List<ListaPrelievoHeader>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT ID, DataCreazione, Operatore, Stato
            FROM dbo.XT_LISTA_DI_PRELIEVO
            WHERE (@HideCompleted = 0 OR UPPER(LTRIM(RTRIM(ISNULL(Stato, '')))) NOT IN (
                'COMPLETATA', 'CHIUSA', 'COMPLETA', 'CHIUSO', 'COMPLETATO', 'FATTA', 'DONE', 'CLOSED'
            ))
            ORDER BY ID DESC
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@HideCompleted", hideCompleted ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = ReadNullableInt(reader, 0);
            if (!id.HasValue || id.Value <= 0)
            {
                continue;
            }

            rows.Add(new ListaPrelievoHeader
            {
                Id = id.Value,
                DataCreazione = ReadNullableDateTime(reader, 1) ?? DateTime.MinValue,
                Operatore = ReadNullableString(reader, 2) ?? string.Empty,
                Stato = ReadNullableString(reader, 3) ?? string.Empty
            });
        }

        return rows;
    }

    public async Task<int> CreateListaPrelievoAsync(string operatore, string stato, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var dataCreazioneType = await GetColumnTypeAsync(connection, "XT_LISTA_DI_PRELIEVO", "DataCreazione", cancellationToken);
        var operatoreType = await GetColumnTypeAsync(connection, "XT_LISTA_DI_PRELIEVO", "Operatore", cancellationToken);
        var statoType = await GetColumnTypeAsync(connection, "XT_LISTA_DI_PRELIEVO", "Stato", cancellationToken);

        const string query = """
            DECLARE @IDLISTA int;
            EXEC @IDLISTA = GetProgressivo 'dbo.XT_LISTA_DI_PRELIEVO';

            INSERT INTO dbo.XT_LISTA_DI_PRELIEVO (ID, DataCreazione, Operatore, Stato)
            VALUES (@IDLISTA, @DataCreazione, @Operatore, @Stato);

            SELECT @IDLISTA;
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@DataCreazione", CoerceDateForColumnType(DateTime.Now, dataCreazioneType));
        command.Parameters.AddWithValue("@Operatore", CoerceForColumnType(operatore.Trim(), operatoreType));
        command.Parameters.AddWithValue("@Stato", CoerceForColumnType(stato.Trim(), statoType));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null || id == DBNull.Value)
        {
            throw new InvalidOperationException("GetProgressivo non ha restituito un ID lista valido.");
        }

        return Convert.ToInt32(id);
    }

    public async Task<IReadOnlyList<ListaPrelievoPiano>> GetPianiAssegnatiListaAsync(int idLista, CancellationToken cancellationToken)
    {
        var rows = new List<ListaPrelievoPiano>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var hasPlacche = await ColumnExistsAsync(connection, "XV_LISTA_PRELIEVO_CLIENTI_MATERIALE", "Placche", cancellationToken);
        var query = hasPlacche
            ? """
                SELECT
                    v.IDNesting as idnes,
                    v.Bolla,
                    v.CodiceNesting as NSCOD,
                    ISNULL(LTRIM(RTRIM(mac.MADSC)), '') as Macchina,
                    v.Materiale,
                    v.Lunghezza,
                    v.Larghezza,
                    v.Spessore,
                    v.Clienti,
                    CAST(NULL as varchar(max)) as Attributi,
                    v.Placche as Placche,
                    nes.DTEXP as DataPrevista,
                    nes.NSNOT as Note,
                    rip.Ripetizioni
                FROM dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE v
                INNER JOIN dbo.A_NES nes ON nes.IDNES = v.IDNesting
                LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
                OUTER APPLY (
                    SELECT SUM(e.NMRIP) as Ripetizioni
                    FROM dbo.L_NELM e
                    WHERE e.IDNES = nes.IDNES
                ) rip
                WHERE v.IDLista = @IdLista
                ORDER BY v.IDNesting DESC
                """
            : """
                SELECT
                    v.IDNesting as idnes,
                    v.Bolla,
                    v.CodiceNesting as NSCOD,
                    ISNULL(LTRIM(RTRIM(mac.MADSC)), '') as Macchina,
                    v.Materiale,
                    v.Lunghezza,
                    v.Larghezza,
                    v.Spessore,
                    v.Clienti,
                    CAST(NULL as varchar(max)) as Attributi,
                    CAST(NULL as varchar(max)) as Placche,
                    nes.DTEXP as DataPrevista,
                    nes.NSNOT as Note,
                    rip.Ripetizioni
                FROM dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE v
                INNER JOIN dbo.A_NES nes ON nes.IDNES = v.IDNesting
                LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
                OUTER APPLY (
                    SELECT SUM(e.NMRIP) as Ripetizioni
                    FROM dbo.L_NELM e
                    WHERE e.IDNES = nes.IDNES
                ) rip
                WHERE v.IDLista = @IdLista
                ORDER BY v.IDNesting DESC
                """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdLista", idLista);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapped = MapListaPiano(reader);
            if (mapped.IdNes > 0)
            {
                rows.Add(mapped);
            }
        }

        return rows;
    }

    public async Task<IReadOnlyList<ListaPrelievoPiano>> GetPianiNonAssegnatiListaAsync(int idLista, string? search, CancellationToken cancellationToken)
    {
        var rows = new List<ListaPrelievoPiano>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (3000)
                nes.IDNES,
                nes.OLCOD as Bolla,
                nes.NSCOD,
                ISNULL(LTRIM(RTRIM(mac.MADSC)), '') as Macchina,
                ISNULL(mat.Materiale, '') as Materiale,
                CAST(NULL as decimal(18,3)) as Lunghezza,
                CAST(NULL as decimal(18,3)) as Larghezza,
                CAST(NULL as decimal(18,3)) as Spessore,
                CAST(NULL as varchar(max)) as Clienti,
                CAST(NULL as varchar(max)) as Attributi,
                CAST(NULL as varchar(max)) as Placche,
                nes.DTEXP as DataPrevista,
                nes.NSNOT as Note
            FROM dbo.A_NES nes
            LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
            OUTER APPLY (
                SELECT TOP (1) tmt.TMDSC as Materiale
                FROM dbo.L_PCFO fo
                LEFT JOIN dbo.A_TMT tmt on tmt.tmcod = fo.mtcod
                WHERE fo.IDPTC = nes.IDNES
            ) mat
            WHERE nes.STNES = 4
                AND LEN(LTRIM(RTRIM(ISNULL(nes.MACOD, '')))) BETWEEN 1 AND 2
                --AND nes.DTEXP IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.XT_LISTA_DI_PRELIEVO_RIGHE r
                    WHERE r.IDLista = @IdLista AND r.IDNesting = nes.IDNES
                )
                AND (
                    @Search IS NULL
                    OR nes.OLCOD LIKE '%' + @Search + '%'
                    OR nes.NSCOD LIKE '%' + @Search + '%'
                    OR mat.Materiale LIKE '%' + @Search + '%'
                )
            ORDER BY nes.IDNES DESC
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdLista", idLista);
        command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapped = MapListaPiano(reader);
            if (mapped.IdNes > 0)
            {
                rows.Add(mapped);
            }
        }

        return rows;
    }

    public async Task<bool> AddPianoToListaByBollaAsync(int idLista, string bolla, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (1) nes.IDNES
            FROM dbo.A_NES nes
            WHERE nes.OLCOD = @Bolla
              AND nes.STNES = 4
              AND LEN(LTRIM(RTRIM(ISNULL(nes.MACOD, '')))) BETWEEN 1 AND 2
              --AND nes.DTEXP IS NOT NULL
            ORDER BY nes.IDNES DESC
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Bolla", bolla.Trim());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            return false;
        }

        await AddPianoToListaAsync(idLista, Convert.ToInt32(result), cancellationToken);
        return true;
    }

    public async Task AddPianoToListaAsync(int idLista, int idNes, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            INSERT INTO dbo.XT_LISTA_DI_PRELIEVO_RIGHE (IDLista, IDNesting)
            SELECT @IdLista, @IdNes
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.XT_LISTA_DI_PRELIEVO_RIGHE r
                WHERE r.IDLista = @IdLista AND r.IDNesting = @IdNes
            )
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdLista", idLista);
        command.Parameters.AddWithValue("@IdNes", idNes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemovePianoFromListaAsync(int idLista, int idNes, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            DELETE FROM dbo.XT_LISTA_DI_PRELIEVO_RIGHE
            WHERE IDLista = @IdLista AND IDNesting = @IdNes
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@IdLista", idLista);
        command.Parameters.AddWithValue("@IdNes", idNes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PianoInfoRow>> CercaInfoPianoAsync(string ricerca, CancellationToken cancellationToken)
    {
        var rows = new List<PianoInfoRow>();
        if (string.IsNullOrWhiteSpace(ricerca))
        {
            return rows;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string query = """
            SELECT TOP (50)
                nes.IDNES,
                LTRIM(RTRIM(ISNULL(nes.OLCOD, ''))) as Bolla,
                LTRIM(RTRIM(ISNULL(nes.NSCOD, ''))) as CodiceNesting,
                ISNULL(LTRIM(RTRIM(mac.MADSC)), '') as Macchina,
                nes.DTEXP as DataPrevista,
                lista.ID as IdListaCollegata,
                lista.Operatore as ListaOperatore,
                lista.Stato as ListaStato,
                lista.DataCreazione as ListaDataCreazione
            FROM dbo.A_NES nes
            LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
            LEFT JOIN dbo.XT_LISTA_DI_PRELIEVO_RIGHE r ON r.IDNesting = nes.IDNES
            LEFT JOIN dbo.XT_LISTA_DI_PRELIEVO lista ON lista.ID = r.IDLista
            WHERE nes.OLCOD = @Ricerca OR nes.NSCOD = @Ricerca
            ORDER BY nes.IDNES DESC
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Ricerca", ricerca.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var idNes = ReadNullableInt(reader, 0);
            if (!idNes.HasValue)
            {
                continue;
            }

            rows.Add(new PianoInfoRow
            {
                IdNes = idNes.Value,
                Bolla = ReadNullableString(reader, 1) ?? string.Empty,
                CodiceNesting = ReadNullableString(reader, 2) ?? string.Empty,
                Macchina = ReadNullableString(reader, 3) ?? string.Empty,
                DataPrevista = ReadNullableDateTime(reader, 4),
                IdListaCollegata = ReadNullableInt(reader, 5),
                ListaOperatore = ReadNullableString(reader, 6),
                ListaStato = ReadNullableString(reader, 7),
                ListaDataCreazione = ReadNullableDateTime(reader, 8)
            });
        }

        return rows;
    }

    public async Task<bool> DeleteListaPrelievoAsync(int idLista, string operatore, CancellationToken cancellationToken)
    {
        if (idLista <= 0 || string.IsNullOrWhiteSpace(operatore))
        {
            return false;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        const string verify = """
            SELECT COUNT(1)
            FROM dbo.XT_LISTA_DI_PRELIEVO
            WHERE ID = @IdLista
              AND UPPER(LTRIM(RTRIM(ISNULL(Operatore, '')))) = UPPER(LTRIM(RTRIM(ISNULL(@Operatore, ''))))
            """;

        await using (var verifyCmd = new SqlCommand(verify, connection, (SqlTransaction)tx))
        {
            verifyCmd.Parameters.AddWithValue("@IdLista", idLista);
            verifyCmd.Parameters.AddWithValue("@Operatore", operatore.Trim());
            var count = Convert.ToInt32(await verifyCmd.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return false;
            }
        }

        const string deleteRighe = """
            DELETE FROM dbo.XT_LISTA_DI_PRELIEVO_RIGHE
            WHERE IDLista = @IdLista
            """;

        await using (var delRighe = new SqlCommand(deleteRighe, connection, (SqlTransaction)tx))
        {
            delRighe.Parameters.AddWithValue("@IdLista", idLista);
            await delRighe.ExecuteNonQueryAsync(cancellationToken);
        }

        const string deleteLista = """
            DELETE FROM dbo.XT_LISTA_DI_PRELIEVO
            WHERE ID = @IdLista
            """;

        await using (var delLista = new SqlCommand(deleteLista, connection, (SqlTransaction)tx))
        {
            delLista.Parameters.AddWithValue("@IdLista", idLista);
            var affected = await delLista.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<MaterialAnalysisResult> AnalizzaMaterialeAsync(MaterialAnalysisRequest request, CancellationToken cancellationToken)
    {
        var result = new MaterialAnalysisResult();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("dbo.X_ProductionManager_Material_Analysis", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = 120
        };

        command.Parameters.AddWithValue("@JobTicketCode", AsDbValue(request.Bolla));
        command.Parameters.AddWithValue("@NestingId", request.IdNesting.HasValue ? request.IdNesting.Value : DBNull.Value);
        command.Parameters.AddWithValue("@NestingCode", AsDbValue(request.CodiceNesting));
        command.Parameters.AddWithValue("@TrackingCode", AsDbValue(request.Rintracciabilita));
        command.Parameters.AddWithValue("@SoloDisponibili", request.SoloDisponibili ? 1 : 0);

        var jobsTable = new System.Data.DataTable();
        jobsTable.Columns.Add("JobOrderID", typeof(int));
        foreach (var idLav in request.Lavorazioni ?? [])
        {
            jobsTable.Rows.Add(idLav);
        }

        var jobsParam = command.Parameters.AddWithValue("@Jobs", jobsTable);
        jobsParam.SqlDbType = System.Data.SqlDbType.Structured;
        jobsParam.TypeName = "dbo.JobOrderTable";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // 1: anomalie
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Anomalie.Add(new MaterialAnomaly
            {
                Code = ReadStringByName(reader, "Code") ?? string.Empty,
                Severity = ReadStringByName(reader, "Severity") ?? string.Empty,
                Message = ReadStringByName(reader, "Message") ?? string.Empty,
                Detail = ReadStringByName(reader, "Detail")
            });
        }

        // 2: materiale minimo (sintesi)
        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            result.MaterialeMinimo = new MaterialeMinimo
            {
                IdNesting = ReadIntByName(reader, "NestingId"),
                Bolla = ReadStringByName(reader, "JobTicketCode"),
                NumLavorazioni = ReadIntByName(reader, "NumLavorazioni") ?? 0,
                NumParti = ReadIntByName(reader, "NumParti") ?? 0,
                Spessore = ReadDecimalByName(reader, "Spessore"),
                NumSpessoriDistinti = ReadIntByName(reader, "NumSpessoriDistinti") ?? 0,
                SpessoriRichiesti = ReadStringByName(reader, "SpessoriRichiesti"),
                QualitaAmmesse = ReadStringByName(reader, "QualitaAmmesse"),
                FormaX = ReadDecimalByName(reader, "FormaX"),
                FormaY = ReadDecimalByName(reader, "FormaY")
            };
        }

        // 3: qualita ammesse
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.MaterialeMinimo?.Qualita.Add(new MaterialQuality
                {
                    Code = ReadStringByName(reader, "Code") ?? string.Empty,
                    Description = ReadStringByName(reader, "Description")?.Trim()
                });
            }
        }

        // 4: attributi richiesti
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.MaterialeMinimo?.Attributi.Add(new AttributoRichiesto
                {
                    ColumnName = ReadStringByName(reader, "ColumnName") ?? string.Empty,
                    JdeCode = ReadStringByName(reader, "JDECode")?.Trim(),
                    Description = ReadStringByName(reader, "Description")?.Trim(),
                    RequiredCode = ReadStringByName(reader, "RequiredCode")?.Trim(),
                    RequiredWeight = ReadDecimalByName(reader, "RequiredWeight"),
                    FilterType = ReadStringByName(reader, "FilterType")?.Trim(),
                    Obbligatorio = (ReadIntByName(reader, "Obbligatorio") ?? 0) == 1
                });
            }
        }

        // 5: lamiere disponibili oppure esito validazione
        var isValidazione = !string.IsNullOrWhiteSpace(request.Rintracciabilita);
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (isValidazione)
                {
                    result.Validazione = new SheetValidation
                    {
                        TrackingId = ReadIntByName(reader, "TrackingId") ?? 0,
                        TrackingCode = ReadStringByName(reader, "TrackingCode") ?? string.Empty,
                        PartCode = ReadStringByName(reader, "PartCode")?.Trim(),
                        PartDescription = ReadStringByName(reader, "PartDescription")?.Trim(),
                        Thickness = ReadDecimalByName(reader, "Thickness"),
                        StoreCode = ReadStringByName(reader, "StoreCode")?.Trim(),
                        LocationCode = ReadStringByName(reader, "LocationCode")?.Trim(),
                        GiacenzaResidua = ReadDecimalByName(reader, "WarehouseResidualQuantity"),
                        QualityCode = ReadStringByName(reader, "QualityCode")?.Trim(),
                        Compatible = ReadIntByName(reader, "Compatible") ?? 0,
                        DimX = ReadDecimalByName(reader, "DimX"),
                        DimY = ReadDecimalByName(reader, "DimY"),
                        SpessoreOk = (ReadIntByName(reader, "SpessoreOk") ?? 0) == 1,
                        QualitaOk = (ReadIntByName(reader, "QualitaOk") ?? 0) == 1,
                        AttributiOk = (ReadIntByName(reader, "AttributiOk") ?? 0) == 1,
                        DimensioniOk = (ReadIntByName(reader, "DimensioniOk") ?? 0) == 1
                    };
                }
                else
                {
                    result.Lamiere.Add(new AvailableSheet
                    {
                        TrackingId = ReadIntByName(reader, "TrackingId") ?? 0,
                        TrackingCode = ReadStringByName(reader, "TrackingCode") ?? string.Empty,
                        PartCode = ReadStringByName(reader, "PartCode")?.Trim(),
                        PartDescription = ReadStringByName(reader, "PartDescription")?.Trim(),
                        Thickness = ReadDecimalByName(reader, "Thickness"),
                        StoreCode = ReadStringByName(reader, "StoreCode")?.Trim(),
                        LocationCode = ReadStringByName(reader, "LocationCode")?.Trim(),
                        GiacenzaResidua = ReadDecimalByName(reader, "WarehouseResidualQuantity"),
                        QualityCode = ReadStringByName(reader, "QualityCode")?.Trim(),
                        Compatible = ReadIntByName(reader, "Compatible") ?? 0,
                        DimX = ReadDecimalByName(reader, "DimX"),
                        DimY = ReadDecimalByName(reader, "DimY"),
                        IsGhost = (ReadIntByName(reader, "IsGhost") ?? 0) == 1
                    });
                }
            }
        }

        // 6: caratteristiche non conformi
        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.CaratteristicheNonConformi.Add(new AttributeFailure
                {
                    ColumnName = ReadStringByName(reader, "ColumnName") ?? string.Empty,
                    JdeCode = ReadStringByName(reader, "JDECode")?.Trim(),
                    Description = ReadStringByName(reader, "Description")?.Trim(),
                    ValoreLamiera = ReadStringByName(reader, "SheetCode")?.Trim(),
                    PesoLamiera = ReadDecimalByName(reader, "SheetWeight"),
                    ValoreRichiesto = ReadStringByName(reader, "RequiredCode")?.Trim(),
                    PesoRichiesto = ReadDecimalByName(reader, "RequiredWeight"),
                    Status = ReadIntByName(reader, "Status") ?? 0,
                    Obbligatorio = (ReadIntByName(reader, "Obbligatorio") ?? 0) == 1
                });
            }
        }

        return result;
    }

    private static object AsDbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? ReadStringByName(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return ReadNullableString(reader, ordinal);
    }

    private static int? ReadIntByName(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return ReadNullableInt(reader, ordinal);
    }

    private static decimal? ReadDecimalByName(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return ReadNullableDecimal(reader, ordinal);
    }

    private static ListaPrelievoPiano MapListaPiano(SqlDataReader reader)
    {
        var idNes = ReadNullableInt(reader, 0);
        if (!idNes.HasValue)
        {
            return new ListaPrelievoPiano();
        }

        return new ListaPrelievoPiano
        {
            IdNes = idNes.Value,
            Bolla = ReadNullableString(reader, 1) ?? string.Empty,
            CodiceNesting = ReadNullableString(reader, 2) ?? string.Empty,
            Macchina = ReadNullableString(reader, 3) ?? string.Empty,
            Materiale = ReadNullableString(reader, 4) ?? string.Empty,
            Lunghezza = ReadNullableDecimal(reader, 5),
            Larghezza = ReadNullableDecimal(reader, 6),
            Spessore = ReadNullableDecimal(reader, 7),
            Clienti = ReadNullableString(reader, 8),
            Attributi = ReadNullableString(reader, 9),
            Placche = ReadNullableString(reader, 10),
            DataPrevista = ReadNullableDateTime(reader, 11),
            Note = ReadNullableString(reader, 12),
            Ripetizioni = reader.FieldCount > 13 ? ReadNullableInt(reader, 13) : null
        };
    }

    private static string? ReadNullableString(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToString(reader.GetValue(ordinal));
    }

    private static decimal? ReadNullableDecimal(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            string s when TryParseTempoDecimal(s, out var parsed) => parsed,
            _ => Convert.ToDecimal(value)
        };
    }

    private static int? ReadNullableInt(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            decimal d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => Convert.ToInt32(value)
        };
    }

    private static bool TryParseTempoDecimal(string input, out decimal value)
    {
        value = 0;
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantDecimal))
        {
            value = invariantDecimal;
            return true;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out var italianDecimal))
        {
            value = italianDecimal;
            return true;
        }

        var parts = trimmed.Split(':');
        if (parts.Length is 2 or 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && (parts.Length == 2 || int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var seconds = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
            value = hours + (minutes / 60m) + (seconds / 3600m);
            return true;
        }

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var ts))
        {
            value = (decimal)ts.TotalHours;
            return true;
        }

        return false;
    }

    private static DateTime? ReadNullableDateTime(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dt => dt,
            int i when DateTime.TryParseExact(i.ToString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedInt) => parsedInt,
            long l when DateTime.TryParseExact(l.ToString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedLong) => parsedLong,
            string s when DateTime.TryParse(s, out var parsed) => parsed,
            _ => Convert.ToDateTime(value)
        };
    }

    private static async Task<string?> GetColumnTypeAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT 1
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result != DBNull.Value;
    }

    private static object CoerceForColumnType(string value, string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return value;
        }

        var type = sqlType.Trim().ToLowerInvariant();
        if (type is "int" or "bigint" or "smallint" or "tinyint")
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        return value;
    }

    private static object CoerceDateForColumnType(DateTime value, string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return value;
        }

        var type = sqlType.Trim().ToLowerInvariant();
        if (type is "int" or "bigint" or "smallint" or "tinyint")
        {
            return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        if (type.Contains("char") || type is "text" or "ntext")
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return value;
    }
}

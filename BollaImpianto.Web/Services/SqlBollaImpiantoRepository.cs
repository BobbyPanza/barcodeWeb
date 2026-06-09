using System.Globalization;
using BollaImpianto.Web.Models;
using Microsoft.Data.SqlClient;

namespace BollaImpianto.Web.Services;

public sealed class SqlBollaImpiantoRepository(IConfiguration configuration) : IBollaImpiantoRepository
{
    private const string MacchinaGenericaPlasma = "DLG-ALL_PLASMA";

    private readonly string _connectionString = configuration.GetConnectionString("MyDatabase")
        ?? throw new InvalidOperationException("Connection string 'MyDatabase' non configurata.");

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
        var hasPlacche = await ColumnExistsAsync(connection, "XV_LISTA_PRELIEVO_CLIENTI_MATERIALE", "Placche", cancellationToken);

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
            ? "\nOUTER APPLY (SELECT TOP 1 vp.Placche FROM dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE vp WHERE vp.IDNesting = src.IDNES) plc"
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
                    nes.NSNOT as Note
                FROM dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE v
                INNER JOIN dbo.A_NES nes ON nes.IDNES = v.IDNesting
                LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
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
                    nes.NSNOT as Note
                FROM dbo.XV_LISTA_PRELIEVO_CLIENTI_MATERIALE v
                INNER JOIN dbo.A_NES nes ON nes.IDNES = v.IDNesting
                LEFT JOIN dbo.A_MAC mac ON mac.MACOD = nes.MACOD
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
            Note = ReadNullableString(reader, 12)
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

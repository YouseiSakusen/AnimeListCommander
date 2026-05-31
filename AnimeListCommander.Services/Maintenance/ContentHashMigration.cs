using System.Data.SQLite;
using System.Text;
using AnimeListCommander.Contexts;
using Dapper;

namespace AnimeListCommander.Maintenance;

/// <summary>
/// AnimeWorks テーブルの ContentHash を一括再計算する一時メンテナンス処理です。
/// </summary>
public class ContentHashMigration
{
    private readonly ApplicationContext applicationContext;

    /// <summary>
    /// <see cref="ContentHashMigration"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="applicationContext">アプリケーションコンテキスト。</param>
    public ContentHashMigration(ApplicationContext applicationContext)
    {
        this.applicationContext = applicationContext;
    }

    /// <summary>
    /// AnimeWorks テーブルの全レコードについて ContentHash を再計算し、DB を更新します。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task RecalculateAllAsync(CancellationToken ct = default)
    {
        using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
        await connection.OpenAsync(ct);

        var works = await selectAllAnimeWorksAsync(connection);

        var workIds = works.Select(w => w.Id).ToList();
        var casts = await selectCastsByWorkIdsAsync(connection, workIds);
        var staffs = await selectStaffsByWorkIdsAsync(connection, workIds);

        var castsLookup = casts.ToLookup(c => c.AnimeWorkId);
        var staffsLookup = staffs.ToLookup(s => s.AnimeWorkId);

        foreach (var work in works)
        {
            work.Casts = castsLookup[work.Id].ToList();
            work.Staffs = staffsLookup[work.Id].ToList();
        }

        var updateSql = "UPDATE AnimeWorks SET ContentHash = @ContentHash WHERE Id = @Id";

        int count = 0;
        foreach (var work in works)
        {
            ct.ThrowIfCancellationRequested();

            var newHash = work.CalculateContentHash();
            await connection.ExecuteAsync(updateSql, new { ContentHash = newHash, work.Id });
            count++;
        }

        Console.WriteLine($"[ContentHashMigration] ContentHash 再計算完了: {count} 件");
    }

    private static async Task<List<AnimeWork>> selectAllAnimeWorksAsync(SQLiteConnection connection)
    {
        var sql = new StringBuilder();
        sql.AppendLine(" SELECT ");
        sql.AppendLine("      Id ");
        sql.AppendLine("    , Year ");
        sql.AppendLine("    , SeasonID ");
        sql.AppendLine("    , SortIndex ");
        sql.AppendLine("    , NormalizedTitle ");
        sql.AppendLine("    , Title ");
        sql.AppendLine("    , AnimateHeaderTitle ");
        sql.AppendLine("    , MyTitle ");
        sql.AppendLine("    , Title_Ruby ");
        sql.AppendLine("    , Company ");
        sql.AppendLine("    , Production ");
        sql.AppendLine("    , ThemeSongs ");
        sql.AppendLine("    , Original ");
        sql.AppendLine("    , BroadcastText ");
        sql.AppendLine("    , Broadcast ");
        sql.AppendLine("    , FirstBroadcast ");
        sql.AppendLine("    , ExportFileName ");
        sql.AppendLine("    , MetaTitleKana ");
        sql.AppendLine("    , MetaBroadcastKana ");
        sql.AppendLine("    , OfficialSiteUrl ");
        sql.AppendLine("    , OfficialPageTitle ");
        sql.AppendLine("    , WikiUrl ");
        sql.AppendLine("    , DirectoryName ");
        sql.AppendLine("    , ContentHash ");
        sql.AppendLine("    , IsExport ");
        sql.AppendLine("    , IsImport ");
        sql.AppendLine("    , InsertedAt ");
        sql.AppendLine("    , UpdatedAt ");
        sql.AppendLine(" FROM AnimeWorks ");
        sql.AppendLine(" ORDER BY Id ASC ");

        var result = await connection.QueryAsync<AnimeWork>(sql.ToString());
        return result.ToList();
    }

    private static async Task<List<CastInfo>> selectCastsByWorkIdsAsync(SQLiteConnection connection, List<int> workIds)
    {
        var sql = new StringBuilder();
        sql.AppendLine(" SELECT ");
        sql.AppendLine("      AnimeWorkId ");
        sql.AppendLine("    , Name ");
        sql.AppendLine("    , SortOrder ");
        sql.AppendLine("    , IsExport ");
        sql.AppendLine(" FROM Casts ");
        sql.AppendLine(" WHERE AnimeWorkId IN @WorkIds ");
        sql.AppendLine("   AND IsExport = 1 ");
        sql.AppendLine(" ORDER BY SortOrder ASC ");

        var result = await connection.QueryAsync<CastInfo>(sql.ToString(), new { WorkIds = workIds });
        return result.ToList();
    }

    private static async Task<List<StaffInfo>> selectStaffsByWorkIdsAsync(SQLiteConnection connection, List<int> workIds)
    {
        var sql = new StringBuilder();
        sql.AppendLine(" SELECT ");
        sql.AppendLine("      AnimeWorkId ");
        sql.AppendLine("    , Role ");
        sql.AppendLine("    , Name ");
        sql.AppendLine("    , SortOrder ");
        sql.AppendLine("    , IsExport ");
        sql.AppendLine(" FROM Staffs ");
        sql.AppendLine(" WHERE AnimeWorkId IN @WorkIds ");
        sql.AppendLine("   AND IsExport = 1 ");
        sql.AppendLine(" ORDER BY SortOrder ASC ");

        var result = await connection.QueryAsync<StaffInfo>(sql.ToString(), new { WorkIds = workIds });
        return result.ToList();
    }
}

using System.Data.SQLite;
using System.Text;
using AnimeListCommander.Contexts;
using Dapper;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// 作戦データの SQLite への永続化を担うリポジトリです。
/// </summary>
public class OperationsRepository
{
	private readonly ApplicationContext applicationContext;
	private readonly ILogger<OperationsRepository> logger;

	/// <summary>
	/// <see cref="OperationsRepository"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public OperationsRepository(ApplicationContext applicationContext, ILogger<OperationsRepository> logger)
	{
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 作品設定項目マスタの全レコードを表示順に取得します。
	/// </summary>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>表示順に並べた <see cref="WorkSettingItem"/> の読み取り専用リスト。</returns>
	public async ValueTask<IReadOnlyList<WorkSettingItem>> GetWorkSettingItemMastersAsync(CancellationToken ct = default)
	{
		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		var items = await selectWorkSettingItemMastersAsync(connection);

		this.logger.ZLogInformation($"WorkSettingItemMasters 取得完了: {items.Count} 件");

		return items;
	}

	private static async Task<IReadOnlyList<WorkSettingItem>> selectWorkSettingItemMastersAsync(SQLiteConnection connection)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      HeaderName ");
		sql.AppendLine("    , IsOverwriteAllowed ");
		sql.AppendLine("    , DisplayOrder ");
		sql.AppendLine("    , Description ");
		sql.AppendLine(" FROM WorkSettingItemMasters ");
		sql.AppendLine(" ORDER BY DisplayOrder ASC ");

		var result = await connection.QueryAsync<WorkSettingItem>(sql.ToString());
		return result.ToList();
	}

	/// <summary>
	/// 指定クールのアニメ作品リスト（キャスト・スタッフを含む）を取得します。
	/// </summary>
	/// <param name="season">取得対象のクール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>
	/// <paramref name="season"/> に対応し、エクスポート対象の <see cref="AnimeWork"/> を
	/// <c>SortIndex</c> 昇順で並べた読み取り専用リスト。
	/// </returns>
	public async ValueTask<IReadOnlyList<AnimeWork>> GetAnimeWorksBySeasonAsync(Season season, CancellationToken ct = default)
	{
		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		var works = await selectAnimeWorksBySeasonAsync(connection, season);

		if (works.Count == 0)
		{
			this.logger.ZLogInformation($"AnimeWorks 取得完了: 0 件 ({season.Year}-{(int)season.SeasonID})");
			return works;
		}

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

		this.logger.ZLogInformation($"AnimeWorks 取得完了: {works.Count} 件 ({season.Year}-{(int)season.SeasonID})");

		return works;
	}

	private static async Task<List<AnimeWork>> selectAnimeWorksBySeasonAsync(SQLiteConnection connection, Season season)
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
		sql.AppendLine(" WHERE Year = @Year ");
		sql.AppendLine("   AND SeasonID = @SeasonID ");
		sql.AppendLine("   AND IsExport = 1 ");
		sql.AppendLine(" ORDER BY SortIndex ASC ");

		var result = await connection.QueryAsync<AnimeWork>(
			sql.ToString(),
			new { Year = season.Year, SeasonID = (int)season.SeasonID });

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

		var result = await connection.QueryAsync<CastInfo>(
			sql.ToString(),
			new { WorkIds = workIds });

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

		var result = await connection.QueryAsync<StaffInfo>(
			sql.ToString(),
			new { WorkIds = workIds });

		return result.ToList();
	}
}

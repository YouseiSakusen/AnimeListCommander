using System.Data.Common;
using System.Data.SQLite;
using System.Text;
using AnimeListCommander.Contexts;
using AnimeListCommander.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// 偵察データの SQLite への永続化を担うリポジトリです。
/// </summary>
public class IntelligenceRepository
{
	private readonly ApplicationContext applicationContext;
	private readonly ILogger<IntelligenceRepository> logger;

	/// <summary>
	/// <see cref="IntelligenceRepository"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public IntelligenceRepository(ApplicationContext applicationContext, ILogger<IntelligenceRepository> logger)
	{
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 指定クールのアニメ作品リストを SQLite に保存し、保存結果の一覧を返します。
	/// </summary>
	/// <param name="season">保存対象のクール。</param>
	/// <param name="works">保存対象のアニメ作品リスト。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>各作品の保存結果リスト。</returns>
	public async Task<List<SaveResult>> SaveAsync(Season season, List<AnimeWork> works, CancellationToken ct)
	{
		var results = new List<SaveResult>();

		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		foreach (var work in works)
		{
			await using var transaction = await connection.BeginTransactionAsync(ct);
			try
			{
				var existing = await selectExistingAsync(connection, transaction, season, work);
				var hash = work.CalculateContentHash();
				var directoryName = string.IsNullOrWhiteSpace(work.DirectoryName)
					? AnimeTitleNormalizer.ToSafeDirectoryName(work.MyTitle)
					: work.DirectoryName;

				SaveResult result;
				if (existing is null)
				{
					var newId = await insertWorkAsync(connection, transaction, season, work, hash, directoryName);
					await insertCastsAsync(connection, transaction, newId, work.Casts);
					await insertStaffsAsync(connection, transaction, newId, work.Staffs);
					result = new SaveResult { Work = work, Status = SaveStatus.New };
					this.logger.ZLogInfo($"[New] {work.NormalizedTitle}");
				}
				else if (existing.ContentHash != hash)
				{
					await updateWorkAsync(connection, transaction, existing.Id, work, hash, directoryName);
					await deleteCastsAsync(connection, transaction, existing.Id);
					await insertCastsAsync(connection, transaction, existing.Id, work.Casts);
					await deleteStaffsAsync(connection, transaction, existing.Id);
					await insertStaffsAsync(connection, transaction, existing.Id, work.Staffs);
					result = new SaveResult { Work = work, Status = SaveStatus.Updated };
					this.logger.ZLogInfo($"[Updated] {work.NormalizedTitle}");
				}
				else
				{
					await touchWorkAsync(connection, transaction, existing.Id);
					result = new SaveResult { Work = work, Status = SaveStatus.Skipped };
					this.logger.ZLogInfo($"[Skipped] {work.NormalizedTitle}");
				}

				await transaction.CommitAsync(ct);
				results.Add(result);
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync(ct);
				this.logger.ZLogError(ex, $"[Failed] {work.NormalizedTitle}: {ex.Message}");
				results.Add(new SaveResult { Work = work, Status = SaveStatus.Failed, Message = ex.Message });
			}
		}

		this.logger.ZLogInfo($"保存処理完了: New={results.Count(r => r.Status == SaveStatus.New)}, Updated={results.Count(r => r.Status == SaveStatus.Updated)}, Skipped={results.Count(r => r.Status == SaveStatus.Skipped)}, Failed={results.Count(r => r.Status == SaveStatus.Failed)}");

		return results;
	}

	private sealed class ExistingWork
	{
		public int Id { get; set; }
		public string? ContentHash { get; set; }
	}

	private static async Task<ExistingWork?> selectExistingAsync(
		SQLiteConnection connection, DbTransaction transaction, Season season, AnimeWork work)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      Id ");
		sql.AppendLine("    , ContentHash ");
		sql.AppendLine(" FROM AnimeWorks ");
		sql.AppendLine(" WHERE Year = @Year ");
		sql.AppendLine("   AND SeasonID = @SeasonID ");
		sql.AppendLine("   AND NormalizedTitle = @NormalizedTitle ");

		return await connection.QuerySingleOrDefaultAsync<ExistingWork>(
			sql.ToString(),
			new { Year = season.Year, SeasonID = (int)season.SeasonID, work.NormalizedTitle },
			transaction);
	}

	private static async Task<long> insertWorkAsync(
		SQLiteConnection connection, DbTransaction transaction, Season season, AnimeWork work, string hash, string directoryName)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" INSERT INTO AnimeWorks ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      Year ");
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
		sql.AppendLine(" ) ");
		sql.AppendLine(" VALUES ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      @Year ");
		sql.AppendLine("    , @SeasonID ");
		sql.AppendLine("    , @SortIndex ");
		sql.AppendLine("    , @NormalizedTitle ");
		sql.AppendLine("    , @Title ");
		sql.AppendLine("    , @AnimateHeaderTitle ");
		sql.AppendLine("    , @MyTitle ");
		sql.AppendLine("    , @Title_Ruby ");
		sql.AppendLine("    , @Company ");
		sql.AppendLine("    , @Production ");
		sql.AppendLine("    , @ThemeSongs ");
		sql.AppendLine("    , @Original ");
		sql.AppendLine("    , @BroadcastText ");
		sql.AppendLine("    , @Broadcast ");
		sql.AppendLine("    , @FirstBroadcast ");
		sql.AppendLine("    , @ExportFileName ");
		sql.AppendLine("    , @MetaTitleKana ");
		sql.AppendLine("    , @MetaBroadcastKana ");
		sql.AppendLine("    , @OfficialSiteUrl ");
		sql.AppendLine("    , @OfficialPageTitle ");
		sql.AppendLine("    , @WikiUrl ");
		sql.AppendLine("    , @DirectoryName ");
		sql.AppendLine("    , @ContentHash ");
		sql.AppendLine("    , @IsExport ");
		sql.AppendLine("    , @IsImport ");
		sql.AppendLine(" ) ");

		await connection.ExecuteAsync(
			sql.ToString(),
			new
			{
				Year = season.Year,
				SeasonID = (int)season.SeasonID,
				work.SortIndex,
				work.NormalizedTitle,
				work.Title,
				work.AnimateHeaderTitle,
				work.MyTitle,
				work.Title_Ruby,
				work.Company,
				work.Production,
				work.ThemeSongs,
				work.Original,
				work.BroadcastText,
				work.Broadcast,
				work.FirstBroadcast,
				work.ExportFileName,
				work.MetaTitleKana,
				work.MetaBroadcastKana,
						work.OfficialSiteUrl,
							work.OfficialPageTitle,
							work.WikiUrl,
							DirectoryName = directoryName,
							ContentHash = hash,
							IsExport = work.IsExport ? 1 : 0,
							IsImport = work.IsImport ? 1 : 0,
						},
						transaction);

					return await connection.QuerySingleAsync<long>("SELECT last_insert_rowid();", transaction: transaction);
				}

				private static async Task insertCastsAsync(
		SQLiteConnection connection, DbTransaction transaction, long animeWorkId, List<CastInfo> casts)
	{
		if (casts.Count == 0)
		{
			return;
		}

		var sql = new StringBuilder();
		sql.AppendLine(" INSERT INTO Casts ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      AnimeWorkId ");
		sql.AppendLine("    , Name ");
		sql.AppendLine("    , SortOrder ");
		sql.AppendLine("    , IsExport ");
		sql.AppendLine(" ) ");
		sql.AppendLine(" VALUES ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      @AnimeWorkId ");
		sql.AppendLine("    , @Name ");
		sql.AppendLine("    , @SortOrder ");
		sql.AppendLine("    , @IsExport ");
		sql.AppendLine(" ) ");

		await connection.ExecuteAsync(
			sql.ToString(),
			casts.Select(c => new
			{
				AnimeWorkId = animeWorkId,
				c.Name,
				c.SortOrder,
				IsExport = c.IsExport ? 1 : 0,
			}),
			transaction);
	}

	private static async Task insertStaffsAsync(
		SQLiteConnection connection, DbTransaction transaction, long animeWorkId, List<StaffInfo> staffs)
	{
		if (staffs.Count == 0)
		{
			return;
		}

		var sql = new StringBuilder();
		sql.AppendLine(" INSERT INTO Staffs ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      AnimeWorkId ");
		sql.AppendLine("    , Role ");
		sql.AppendLine("    , Name ");
		sql.AppendLine("    , SortOrder ");
		sql.AppendLine("    , IsExport ");
		sql.AppendLine(" ) ");
		sql.AppendLine(" VALUES ");
		sql.AppendLine(" ( ");
		sql.AppendLine("      @AnimeWorkId ");
		sql.AppendLine("    , @Role ");
		sql.AppendLine("    , @Name ");
		sql.AppendLine("    , @SortOrder ");
		sql.AppendLine("    , @IsExport ");
		sql.AppendLine(" ) ");

		await connection.ExecuteAsync(
			sql.ToString(),
			staffs.Select(s => new
			{
				AnimeWorkId = animeWorkId,
				s.Role,
				s.Name,
				s.SortOrder,
				IsExport = s.IsExport ? 1 : 0,
			}),
			transaction);
	}

	private static async Task updateWorkAsync(
		SQLiteConnection connection, DbTransaction transaction, int id, AnimeWork work, string hash, string directoryName)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" UPDATE AnimeWorks ");
		sql.AppendLine("    SET SortIndex = @SortIndex ");
		sql.AppendLine("      , Title = @Title ");
		sql.AppendLine("      , AnimateHeaderTitle = @AnimateHeaderTitle ");
		sql.AppendLine("      , MyTitle = @MyTitle ");
		sql.AppendLine("      , Title_Ruby = @Title_Ruby ");
		sql.AppendLine("      , Company = @Company ");
		sql.AppendLine("      , Production = @Production ");
		sql.AppendLine("      , ThemeSongs = @ThemeSongs ");
		sql.AppendLine("      , Original = @Original ");
		sql.AppendLine("      , BroadcastText = @BroadcastText ");
		sql.AppendLine("      , Broadcast = @Broadcast ");
		sql.AppendLine("      , FirstBroadcast = @FirstBroadcast ");
		sql.AppendLine("      , ExportFileName = @ExportFileName ");
		sql.AppendLine("      , MetaTitleKana = @MetaTitleKana ");
		sql.AppendLine("      , MetaBroadcastKana = @MetaBroadcastKana ");
		sql.AppendLine("      , OfficialSiteUrl = @OfficialSiteUrl ");
		sql.AppendLine("      , OfficialPageTitle = @OfficialPageTitle ");
		sql.AppendLine("      , WikiUrl = @WikiUrl ");
		sql.AppendLine("      , DirectoryName = @DirectoryName ");
		sql.AppendLine("      , ContentHash = @ContentHash ");
		sql.AppendLine("      , IsExport = @IsExport ");
		sql.AppendLine("      , IsImport = @IsImport ");
		sql.AppendLine("      , UpdatedAt = DATETIME('now', 'localtime') ");
		sql.AppendLine(" WHERE Id = @Id ");

		await connection.ExecuteAsync(
			sql.ToString(),
			new
			{
				Id = id,
				work.SortIndex,
				work.Title,
				work.AnimateHeaderTitle,
				work.MyTitle,
				work.Title_Ruby,
				work.Company,
				work.Production,
				work.ThemeSongs,
				work.Original,
				work.BroadcastText,
				work.Broadcast,
				work.FirstBroadcast,
				work.ExportFileName,
				work.MetaTitleKana,
						work.MetaBroadcastKana,
							work.OfficialSiteUrl,
							work.OfficialPageTitle,
							work.WikiUrl,
							DirectoryName = directoryName,
							ContentHash = hash,
							IsExport = work.IsExport ? 1 : 0,
							IsImport = work.IsImport ? 1 : 0,
						},
						transaction);
				}

				private static async Task deleteCastsAsync(SQLiteConnection connection, DbTransaction transaction, int animeWorkId)
				{
		await connection.ExecuteAsync(
			" DELETE FROM Casts WHERE AnimeWorkId = @AnimeWorkId ",
			new { AnimeWorkId = animeWorkId },
			transaction);
	}

	private static async Task deleteStaffsAsync(SQLiteConnection connection, DbTransaction transaction, int animeWorkId)
	{
		await connection.ExecuteAsync(
			" DELETE FROM Staffs WHERE AnimeWorkId = @AnimeWorkId ",
			new { AnimeWorkId = animeWorkId },
			transaction);
	}

	private static async Task touchWorkAsync(SQLiteConnection connection, DbTransaction transaction, int id)
	{
		await connection.ExecuteAsync(
			" UPDATE AnimeWorks SET UpdatedAt = DATETIME('now', 'localtime') WHERE Id = @Id ",
			new { Id = id },
			transaction);
	}
}

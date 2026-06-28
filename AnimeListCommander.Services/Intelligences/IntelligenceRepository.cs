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

		foreach (var work in works.Where(w => w.IsImport))
		{
			await using var transaction = await connection.BeginTransactionAsync(ct);
			try
			{
				var existing = await this.selectExistingAsync(connection, transaction, season, work);
				var hash = work.CalculateContentHash();
				var directoryName = string.IsNullOrWhiteSpace(work.DirectoryName)
					? AnimeTitleNormalizer.ToSafeDirectoryName(work.MyTitle)
					: work.DirectoryName;

				SaveResult result;
				if (existing is null)
				{
					var newId = await this.insertWorkAsync(connection, transaction, season, work, hash, directoryName);
					await this.insertCastsAsync(connection, transaction, newId, work.Casts);
					await this.insertStaffsAsync(connection, transaction, newId, work.Staffs);
					result = new SaveResult { Work = work, Status = SaveStatus.New };
					this.logger.ZLogInfo($"[New] {work.NormalizedTitle}");
				}
				else if (existing.HasXcf == 1)
				{
					// HasXcf=true: Work-settings.txt 由来の保護フィールドは更新しない
					var xcfDiffs = this.collectXcfDiffs(existing, work);

					// ContentHash はスクレイピング取得値で再計算するが、保護フィールド差分があっても
					// 実際に DB へ反映しないためハッシュも保護フィールドを除いた hash を用いる
					var xcfHash = calculateHashWithoutXcfFields(existing, work);

					if (existing.ContentHash != xcfHash)
						{
							await this.updateWorkXcfAsync(connection, transaction, existing.Id, work, xcfHash, existing.ThemeSongs);
							await this.deleteCastsAsync(connection, transaction, existing.Id);
							await this.insertCastsAsync(connection, transaction, existing.Id, work.Casts);

							// Staffs は既存が存在しない場合のみスクレイピング結果を登録
							if (existing.StaffCount == 0)
							{
								await this.insertStaffsAsync(connection, transaction, existing.Id, work.Staffs);
							}

							var statusLabel = xcfDiffs.Count > 0 ? "[Updated(XCF)]" : "[Updated(XCF)]";
							result = new SaveResult { Work = work, Status = SaveStatus.Updated, XcfDiffs = xcfDiffs };
							this.logger.ZLogInfo($"{statusLabel} {work.NormalizedTitle}");
						}
					else
					{
						var newIsImport = work.IsImport ? 1 : 0;
						if (existing.IsImport != newIsImport)
							await this.updateIsImportAsync(connection, transaction, existing.Id, newIsImport);
						else
							await this.touchWorkAsync(connection, transaction, existing.Id);
						result = new SaveResult { Work = work, Status = SaveStatus.Skipped, XcfDiffs = xcfDiffs };
						this.logger.ZLogInfo($"[Skipped(XCF)] {work.NormalizedTitle}");
					}
				}
				else if (existing.ContentHash != hash)
				{
					await this.updateWorkAsync(connection, transaction, existing.Id, work, hash, directoryName);
					await this.deleteCastsAsync(connection, transaction, existing.Id);
					await this.insertCastsAsync(connection, transaction, existing.Id, work.Casts);
					await this.deleteStaffsAsync(connection, transaction, existing.Id);
					await this.insertStaffsAsync(connection, transaction, existing.Id, work.Staffs);
					result = new SaveResult { Work = work, Status = SaveStatus.Updated };
					this.logger.ZLogInfo($"[Updated] {work.NormalizedTitle}");
				}
				else
				{
					// IsImport はハッシュ対象外のため、差分がある場合は単独で UPDATE する
					var newIsImport = work.IsImport ? 1 : 0;
					if (existing.IsImport != newIsImport)
						await this.updateIsImportAsync(connection, transaction, existing.Id, newIsImport);
					else
						await this.touchWorkAsync(connection, transaction, existing.Id);
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

	/// <summary>
	/// 指定 ID のアニメ作品レコードの Title のみを更新します。
	/// </summary>
	/// <param name="id">更新対象レコードの ID。</param>
	/// <param name="title">更新後のタイトル。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>更新件数。</returns>
	public async Task<int> UpdateTitleAsync(int id, string title, CancellationToken ct)
	{
		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		return await connection.ExecuteAsync(
			"""
			UPDATE AnimeWorks
			   SET Title = @Title
				 , UpdatedAt = DATETIME('now', 'localtime')
			 WHERE Id = @Id
			""",
			new { Id = id, Title = title });
	}

	/// <summary>
	/// Work-Settings.txt の内容を指定 ID のアニメ作品レコードに同期します。
	/// MyTitle / Original / ExportFileName / Kana系は DB 上で NULL または空文字の場合のみ更新します。
	/// </summary>
	/// <param name="id">更新対象レコードの ID。</param>
	/// <param name="myTitle">画像用タイトル（#TITLE）。</param>
	/// <param name="exportFileName">エクスポートファイル名（#EXPORT_FILENAME）。</param>
	/// <param name="metaTitleKana">メタタイトルかな（#META_TITLE_KANA）。</param>
	/// <param name="metaBroadcastKana">メタ放送かな（#META_BROADCAST_KANA）。</param>
	/// <param name="original">原作（#ORIGINAL）。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>更新件数。</returns>
	public async Task<int> UpdateFromWorkSettingsAsync(
		int id,
		string? myTitle,
		string? exportFileName,
		string? metaTitleKana,
		string? metaBroadcastKana,
		string? original,
		CancellationToken ct)
	{
		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		return await connection.ExecuteAsync(
			"""
			UPDATE AnimeWorks
			   SET MyTitle = CASE
								WHEN MyTitle IS NULL OR MyTitle = '' THEN @MyTitle
								ELSE MyTitle
							 END
				 , ExportFileName = CASE
									   WHEN ExportFileName IS NULL OR ExportFileName = '' THEN @ExportFileName
									   ELSE ExportFileName
									END
				 , MetaTitleKana = CASE
									  WHEN MetaTitleKana IS NULL OR MetaTitleKana = '' THEN @MetaTitleKana
									  ELSE MetaTitleKana
								   END
				 , MetaBroadcastKana = CASE
										  WHEN MetaBroadcastKana IS NULL OR MetaBroadcastKana = '' THEN @MetaBroadcastKana
										  ELSE MetaBroadcastKana
									   END
				 -- Original は手動入力が前提のため、既存値がある場合は上書きしない
				 , Original = CASE
								  WHEN Original IS NULL OR Original = '' THEN @Original
								  ELSE Original
							   END
				 , UpdatedAt = DATETIME('now', 'localtime')
			 WHERE Id = @Id
			""",
			new
			{
				Id = id,
				MyTitle = myTitle ?? string.Empty,
				ExportFileName = exportFileName ?? string.Empty,
				MetaTitleKana = metaTitleKana ?? string.Empty,
				MetaBroadcastKana = metaBroadcastKana ?? string.Empty,
				Original = original ?? string.Empty,
			});
	}

	/// <summary>
	/// XCF 同期用: Work-Settings.txt の内容を指定 ID のアニメ作品レコードに同期します。
	/// HasXcf を true に設定し、DirectoryName・MyTitle 等を Work-settings.txt の値で上書きします。
	/// また、DB の Staffs を完全に置き換えます（既存を全削除後、Work-settings の内容で再登録）。
	/// </summary>
	/// <param name="id">更新対象レコードの ID。</param>
	/// <param name="directoryName">ディレクトリ名。</param>
	/// <param name="myTitle">画像用タイトル（#TITLE）。</param>
	/// <param name="titleRuby">タイトルルビ（#TITLE_RUBY）。</param>
	/// <param name="exportFileName">エクスポートファイル名（#EXPORT_FILENAME）。</param>
	/// <param name="metaTitleKana">メタタイトルかな（#META_TITLE_KANA）。</param>
	/// <param name="metaBroadcastKana">メタ放送かな（#META_BROADCAST_KANA）。</param>
	/// <param name="original">原作（#ORIGINAL）。</param>
	/// <param name="broadcastText">放送テキスト（#BROADCAST_TEXT）。</param>
	/// <param name="broadcastLogo">放送ロゴ（#BROADCAST_LOGO）。</param>
	/// <param name="company">会社名（#COMPANY）。</param>
	/// <param name="production">制作会社名（#PRODUCTION_LOGO）。</param>
	/// <param name="themeSongs">主題歌（#THEME_SONG）。</param>
	/// <param name="firstBroadcast">初回放送（#FIRST_BROADCAST）。</param>
	/// <param name="staffEntries">work-settings.txt の #STAFF から読んだ (Role, Name) の一覧。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>更新件数。</returns>
	public async Task<int> UpdateFromWorkSettingsWithXcfAsync(
		int id,
		string directoryName,
		string? myTitle,
		string? titleRuby,
		string? exportFileName,
		string? metaTitleKana,
		string? metaBroadcastKana,
		string? original,
		string? broadcastText,
		string? broadcastLogo,
		string? company,
		string? production,
		string? themeSongs,
		string? firstBroadcast,
		IReadOnlyList<(string Role, string Name)> staffEntries,
		CancellationToken ct)
	{
		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);
		await using var transaction = await connection.BeginTransactionAsync(ct);

		var affected = await connection.ExecuteAsync(
			"""
			UPDATE AnimeWorks
			   SET HasXcf = 1
				 , DirectoryName = @DirectoryName
				 , MyTitle = @MyTitle
				 , Title_Ruby = @TitleRuby
				 , ExportFileName = @ExportFileName
				 , MetaTitleKana = @MetaTitleKana
				 , MetaBroadcastKana = @MetaBroadcastKana
				 , Original = @Original
				 , BroadcastText = @BroadcastText
				 , Broadcast = @Broadcast
				 , Company = @Company
				 , Production = @Production
				 , ThemeSongs = @ThemeSongs
				 , FirstBroadcast = @FirstBroadcast
				 , UpdatedAt = DATETIME('now', 'localtime')
			 WHERE Id = @Id
			""",
			new
			{
				Id = id,
				DirectoryName = directoryName,
				MyTitle = myTitle ?? string.Empty,
				TitleRuby = titleRuby ?? string.Empty,
				ExportFileName = exportFileName ?? string.Empty,
				MetaTitleKana = metaTitleKana ?? string.Empty,
				MetaBroadcastKana = metaBroadcastKana ?? string.Empty,
				Original = original ?? string.Empty,
				BroadcastText = broadcastText ?? string.Empty,
				Broadcast = broadcastLogo ?? string.Empty,
				Company = company ?? string.Empty,
				Production = production ?? string.Empty,
				ThemeSongs = themeSongs ?? string.Empty,
				FirstBroadcast = firstBroadcast ?? string.Empty,
			},
			transaction);

		// Work-settings.txt の #STAFF で Staffs テーブルを完全に置き換える
		// 既存スタッフをすべて削除
		await connection.ExecuteAsync(
			"DELETE FROM Staffs WHERE AnimeWorkId = @AnimeWorkId",
			new { AnimeWorkId = id },
			transaction);

		// Work-settings から取得したスタッフを登録
		if (staffEntries.Count > 0)
		{
			var staffs = staffEntries
				.Select((entry, index) => new StaffInfo
				{
					AnimeWorkId = id,
					Role = entry.Role,
					Name = entry.Name,
					SortOrder = index + 1,
					IsExport = true,
				})
				.ToList();

			await this.insertStaffsAsync(connection, transaction, id, staffs);
		}

		await transaction.CommitAsync(ct);
		return affected;
	}

	/// <summary>
	/// </summary>
	private sealed class ExistingWork
	{
		/// <summary>
		/// レコードの主キーを取得または設定します。
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// レコードのコンテンツハッシュ値を取得または設定します。
		/// </summary>
		public string? ContentHash { get; set; }

		/// <summary>
		/// インポート対象フラグを取得または設定します（SQLite では 0/1 で保存）。
		/// </summary>
		public int IsImport { get; set; }

		/// <summary>
		/// XCF ファイルが存在するかどうかを取得または設定します（SQLite では 0/1 で保存）。
		/// </summary>
		public int HasXcf { get; set; }

		/// <summary>
		/// 既存のスタッフ数を取得または設定します。HasXcf=true の場合の Staffs 更新制御に使用します。
		/// </summary>
		public int StaffCount { get; set; }

		// Work-settings.txt 由来の保護フィールド
		public string MyTitle { get; set; } = string.Empty;
		public string DirectoryName { get; set; } = string.Empty;
		public string Title_Ruby { get; set; } = string.Empty;
		public string ExportFileName { get; set; } = string.Empty;
		public string MetaTitleKana { get; set; } = string.Empty;
		public string MetaBroadcastKana { get; set; } = string.Empty;
		public string BroadcastText { get; set; } = string.Empty;
		public string Company { get; set; } = string.Empty;
		public string Production { get; set; } = string.Empty;
		public string ThemeSongs { get; set; } = string.Empty;
		public string FirstBroadcast { get; set; } = string.Empty;
		public string Original { get; set; } = string.Empty;
	}

	/// <summary>
	/// 指定クールおよびタイトルに一致する既存レコードを取得します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="season">対象クール。</param>
	/// <param name="work">突合対象のアニメ作品。</param>
	/// <returns>既存レコード。存在しない場合は null。</returns>
	private async Task<ExistingWork?> selectExistingAsync(SQLiteConnection connection, DbTransaction transaction, Season season, AnimeWork work)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      Id ");
		sql.AppendLine("    , ContentHash ");
		sql.AppendLine("    , IsImport ");
		sql.AppendLine("    , HasXcf ");
		sql.AppendLine("    , MyTitle ");
		sql.AppendLine("    , DirectoryName ");
		sql.AppendLine("    , Title_Ruby ");
		sql.AppendLine("    , ExportFileName ");
		sql.AppendLine("    , MetaTitleKana ");
		sql.AppendLine("    , MetaBroadcastKana ");
		sql.AppendLine("    , BroadcastText ");
		sql.AppendLine("    , Company ");
		sql.AppendLine("    , Production ");
		sql.AppendLine("    , ThemeSongs ");
		sql.AppendLine("    , FirstBroadcast ");
		sql.AppendLine("    , Original ");
		sql.AppendLine("    , (SELECT COUNT(*) FROM Staffs WHERE AnimeWorkId = AnimeWorks.Id) AS StaffCount ");
		sql.AppendLine(" FROM AnimeWorks ");
		sql.AppendLine(" WHERE Year = @Year ");
		sql.AppendLine("   AND SeasonID = @SeasonID ");
		sql.AppendLine("   AND NormalizedTitle = @NormalizedTitle ");

		return await connection.QuerySingleOrDefaultAsync<ExistingWork>(
			sql.ToString(),
			new { Year = season.Year, SeasonID = (int)season.SeasonID, work.NormalizedTitle },
			transaction);
	}

	/// <summary>
	/// アニメ作品を AnimeWorks テーブルに INSERT し、新規採番された ID を返します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="season">対象クール。</param>
	/// <param name="work">挿入するアニメ作品情報。</param>
	/// <param name="hash">コンテンツハッシュ値。</param>
	/// <param name="directoryName">ディレクトリ名。</param>
	/// <returns>INSERT されたレコードの ID。</returns>
	private async Task<long> insertWorkAsync(SQLiteConnection connection, DbTransaction transaction, Season season, AnimeWork work, string hash, string directoryName)
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
		sql.AppendLine("    , HasXcf ");
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
		sql.AppendLine("    , @HasXcf ");
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
				HasXcf = work.HasXcf ? 1 : 0,
			},
			transaction);

					return await connection.QuerySingleAsync<long>("SELECT last_insert_rowid();", transaction: transaction);
				}

	/// <summary>
	/// キャスト情報を Casts テーブルに一括 INSERT します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="animeWorkId">親となるアニメ作品の ID。</param>
	/// <param name="casts">挿入するキャスト情報のリスト。</param>
	private async Task insertCastsAsync(SQLiteConnection connection, DbTransaction transaction, long animeWorkId, List<CastInfo> casts)
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

	/// <summary>
	/// スタッフ情報を Staffs テーブルに一括 INSERT します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="animeWorkId">親となるアニメ作品の ID。</param>
	/// <param name="staffs">挿入するスタッフ情報のリスト。</param>
	private async Task insertStaffsAsync(SQLiteConnection connection, DbTransaction transaction, long animeWorkId, List<StaffInfo> staffs)
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

	/// <summary>
	/// HasXcf=true 作品において DB と スクレイピング取得値の差分を収集します。
	/// </summary>
	private List<XcfFieldDiff> collectXcfDiffs(ExistingWork existing, AnimeWork scraped)
	{
		var diffs = new List<XcfFieldDiff>();

		void check(string fieldName, string dbValue, string scrapedValue)
		{
			if (!string.Equals(dbValue, scrapedValue, StringComparison.Ordinal))
				diffs.Add(new XcfFieldDiff { FieldName = fieldName, DbValue = dbValue, ScrapedValue = scrapedValue });
		}

		check("MY_TITLE",            existing.MyTitle,           scraped.MyTitle);
		check("DIRECTORY_NAME",      existing.DirectoryName,     scraped.DirectoryName);
		check("TITLE_RUBY",          existing.Title_Ruby,        scraped.Title_Ruby);
		check("EXPORT_FILENAME",     existing.ExportFileName,    scraped.ExportFileName);
		check("META_TITLE_KANA",     existing.MetaTitleKana,     scraped.MetaTitleKana);
		check("META_BROADCAST_KANA", existing.MetaBroadcastKana, scraped.MetaBroadcastKana);
		check("BROADCAST_TEXT",      existing.BroadcastText,     scraped.BroadcastText);
		check("COMPANY",             existing.Company,           scraped.Company);
		check("PRODUCTION",          existing.Production,        scraped.Production);
		check("THEME_SONG",          existing.ThemeSongs,        scraped.ThemeSongs);
		check("FIRST_BROADCAST",     existing.FirstBroadcast,    scraped.FirstBroadcast);
		check("ORIGINAL",            existing.Original,          scraped.Original);

		// Staffs の差分を検出（DB に Staffs が存在する場合、スクレイピング結果は登録しない）
		if (existing.StaffCount > 0 && scraped.Staffs.Count > 0)
		{
			// DB に Staffs が存在し、スクレイピング結果にも Staff がある場合
			var dbStaffStr = "(既存あり)";
			var scrapedStaffStr = string.Join(", ", scraped.Staffs.Select(s => $"{s.Role}:{s.Name}"));
			check("STAFFS", dbStaffStr, scrapedStaffStr);
		}
		else if (existing.StaffCount == 0 && scraped.Staffs.Count > 0)
		{
			// DB に Staffs がなく、スクレイピング結果に Staff がある場合
			var dbStaffStr = "(なし)";
			var scrapedStaffStr = string.Join(", ", scraped.Staffs.Select(s => $"{s.Role}:{s.Name}"));
			check("STAFFS", dbStaffStr, scrapedStaffStr);
		}

		return diffs;
	}

	/// <summary>
	/// HasXcf=true 作品用: 保護フィールドは DB 値を使ってコンテンツハッシュを計算します。
	/// キャストはスクレイピング取得値、スタッフは DB 値を使います。
	/// </summary>
	private static string calculateHashWithoutXcfFields(ExistingWork existing, AnimeWork scraped)
	{
		var castNames = string.Join("|", scraped.Casts.Select(c => c.Name));
		// Staffs は保護対象のため DB 値は存在しない（再取得しない）。
		// ハッシュの安定性のため staffEntries は空文字とする。
		var raw = scraped.Title + existing.Company + existing.Production
			+ existing.ThemeSongs + existing.Original + existing.BroadcastText
			+ scraped.Broadcast + existing.FirstBroadcast + scraped.OfficialSiteUrl + scraped.OfficialPageTitle
			+ existing.ExportFileName
			+ existing.MetaTitleKana
			+ existing.MetaBroadcastKana
			+ castNames;
		var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}

	/// <summary>
	/// HasXcf=true 作品の ThemeSongs マージロジック。
	/// 既存DBとスクレイピング取得値をマージします。
	/// OP / ED 行の未発表プレースホルダー（「OP：」「ED：」のみ）は補完対象となります。
	/// </summary>
	/// <param name="existingThemeSongs">既存DBの ThemeSongs。</param>
	/// <param name="scrapedThemeSongs">スクレイピング取得値の ThemeSongs。</param>
	/// <returns>マージ後の ThemeSongs。</returns>
	private static string mergeThemeSongs(string? existingThemeSongs, string? scrapedThemeSongs)
	{
		// 既存DBが NULL または空の場合は、スクレイピング値をそのまま使用
		if (string.IsNullOrWhiteSpace(existingThemeSongs))
		{
			return scrapedThemeSongs ?? string.Empty;
		}

		// スクレイピング値が NULL または空の場合は、既存DB値を使用
		if (string.IsNullOrWhiteSpace(scrapedThemeSongs))
		{
			return existingThemeSongs;
		}

		// 既存DB値をパース（改行で分割）
		var existingLines = existingThemeSongs.Split('\n', StringSplitOptions.None);
		var scrapedLines = scrapedThemeSongs.Split('\n', StringSplitOptions.None);

		// OP / ED を抽出（未発表プレースホルダー「OP：」「ED：」と完全な値を区別）
		var existingOpLine = existingLines.FirstOrDefault(l => l.StartsWith("OP："));
		var existingEdLine = existingLines.FirstOrDefault(l => l.StartsWith("ED："));
		var scrapedOpLine = scrapedLines.FirstOrDefault(l => l.StartsWith("OP："));
		var scrapedEdLine = scrapedLines.FirstOrDefault(l => l.StartsWith("ED："));

		// OP / ED 以外の行（他の主題歌がある場合）
		var existingOtherLines = existingLines
			.Where(l => !l.StartsWith("OP：") && !l.StartsWith("ED：") && !string.IsNullOrWhiteSpace(l))
			.ToList();
		var scrapedOtherLines = scrapedLines
			.Where(l => !l.StartsWith("OP：") && !l.StartsWith("ED：") && !string.IsNullOrWhiteSpace(l))
			.ToList();

		var resultLines = new List<string>();

		// OP 行の処理
		var opLine = existingOpLine;
		if (existingOpLine == "OP：")
		{
			// 既存DBの OP が未発表プレースホルダー
			if (!string.IsNullOrEmpty(scrapedOpLine))
			{
				opLine = scrapedOpLine;
			}
		}
		if (!string.IsNullOrEmpty(opLine))
		{
			resultLines.Add(opLine);
		}

		// ED 行の処理
		var edLine = existingEdLine;
		if (existingEdLine == "ED：")
		{
			// 既存DBの ED が未発表プレースホルダー
			if (!string.IsNullOrEmpty(scrapedEdLine))
			{
				edLine = scrapedEdLine;
			}
		}
		if (!string.IsNullOrEmpty(edLine))
		{
			resultLines.Add(edLine);
		}

		// OP / ED 以外の行：既存DB側に値がある場合は優先、ない場合はスクレイピング側を追加
		if (existingOtherLines.Count > 0)
		{
			resultLines.AddRange(existingOtherLines);
		}
		else if (scrapedOtherLines.Count > 0)
		{
			resultLines.AddRange(scrapedOtherLines);
		}

		return string.Join("\n", resultLines);
	}

	/// <summary>
	/// HasXcf=true 作品用: Work-settings.txt 由来の保護フィールドを除いて AnimeWorks を UPDATE します。
	/// 既存値が空でない保護フィールドは更新しません。キャスト・スタッフは呼び出し元で制御します。
	/// ThemeSongs はマージロジックに基づいて補完されます。
	/// </summary>
	private async Task updateWorkXcfAsync(SQLiteConnection connection, DbTransaction transaction, int id, AnimeWork work, string hash, string? existingThemeSongs)
	{
		// ThemeSongs をマージ（OP / ED 行の補完ロジック）
		var mergedThemeSongs = mergeThemeSongs(existingThemeSongs, work.ThemeSongs);

		var sql = new StringBuilder();
		sql.AppendLine(" UPDATE AnimeWorks ");
		sql.AppendLine("    SET SortIndex = @SortIndex ");
		sql.AppendLine("      , Title = @Title ");
		sql.AppendLine("      , AnimateHeaderTitle = @AnimateHeaderTitle ");
		sql.AppendLine("      , MyTitle = CASE ");
		sql.AppendLine("                     WHEN MyTitle IS NULL OR MyTitle = '' THEN @MyTitle ");
		sql.AppendLine("                     ELSE MyTitle ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , Title_Ruby = CASE ");
		sql.AppendLine("                       WHEN Title_Ruby IS NULL OR Title_Ruby = '' THEN @Title_Ruby ");
		sql.AppendLine("                       ELSE Title_Ruby ");
		sql.AppendLine("                     END ");
		sql.AppendLine("      , DirectoryName = CASE ");
		sql.AppendLine("                          WHEN DirectoryName IS NULL OR DirectoryName = '' THEN @DirectoryName ");
		sql.AppendLine("                          ELSE DirectoryName ");
		sql.AppendLine("                        END ");
		sql.AppendLine("      , Company = CASE ");
		sql.AppendLine("                    WHEN Company IS NULL OR Company = '' THEN @Company ");
		sql.AppendLine("                    ELSE Company ");
		sql.AppendLine("                  END ");
		sql.AppendLine("      , Production = CASE ");
		sql.AppendLine("                       WHEN Production IS NULL OR Production = '' THEN @Production ");
		sql.AppendLine("                       ELSE Production ");
		sql.AppendLine("                     END ");
		sql.AppendLine("      , ThemeSongs = @ThemeSongs ");
		sql.AppendLine("      , Original = CASE ");
		sql.AppendLine("                     WHEN Original IS NULL OR Original = '' THEN @Original ");
		sql.AppendLine("                     ELSE Original ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , BroadcastText = CASE ");
		sql.AppendLine("                          WHEN BroadcastText IS NULL OR BroadcastText = '' THEN @BroadcastText ");
		sql.AppendLine("                          ELSE BroadcastText ");
		sql.AppendLine("                        END ");
		sql.AppendLine("      , Broadcast = CASE ");
		sql.AppendLine("                      WHEN Broadcast IS NULL OR Broadcast = '' THEN @Broadcast ");
		sql.AppendLine("                      ELSE Broadcast ");
		sql.AppendLine("                    END ");
		sql.AppendLine("      , FirstBroadcast = CASE ");
		sql.AppendLine("                           WHEN FirstBroadcast IS NULL OR FirstBroadcast = '' THEN @FirstBroadcast ");
		sql.AppendLine("                           ELSE FirstBroadcast ");
		sql.AppendLine("                         END ");
		sql.AppendLine("      , ExportFileName = CASE ");
		sql.AppendLine("                           WHEN ExportFileName IS NULL OR ExportFileName = '' THEN @ExportFileName ");
		sql.AppendLine("                           ELSE ExportFileName ");
		sql.AppendLine("                         END ");
		sql.AppendLine("      , MetaTitleKana = CASE ");
		sql.AppendLine("                          WHEN MetaTitleKana IS NULL OR MetaTitleKana = '' THEN @MetaTitleKana ");
		sql.AppendLine("                          ELSE MetaTitleKana ");
		sql.AppendLine("                        END ");
		sql.AppendLine("      , MetaBroadcastKana = CASE ");
		sql.AppendLine("                              WHEN MetaBroadcastKana IS NULL OR MetaBroadcastKana = '' THEN @MetaBroadcastKana ");
		sql.AppendLine("                              ELSE MetaBroadcastKana ");
		sql.AppendLine("                            END ");
		sql.AppendLine("      , OfficialSiteUrl = @OfficialSiteUrl ");
		sql.AppendLine("      , OfficialPageTitle = @OfficialPageTitle ");
		sql.AppendLine("      , WikiUrl = @WikiUrl ");
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
				work.DirectoryName,
				work.Company,
				work.Production,
				ThemeSongs = mergedThemeSongs,
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
				ContentHash = hash,
				IsExport = work.IsExport ? 1 : 0,
				IsImport = work.IsImport ? 1 : 0,
			},
			transaction);
	}

	/// <summary>
	/// 指定 ID のアニメ作品レコードを UPDATE します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="id">更新対象レコードの ID。</param>
	/// <param name="work">更新後のアニメ作品情報。</param>
	/// <param name="hash">コンテンツハッシュ値。</param>
	/// <param name="directoryName">ディレクトリ名。</param>
	private async Task updateWorkAsync(SQLiteConnection connection, DbTransaction transaction, int id, AnimeWork work, string hash, string directoryName)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" UPDATE AnimeWorks ");
		sql.AppendLine("    SET SortIndex = @SortIndex ");
		sql.AppendLine("      , Title = @Title ");
		sql.AppendLine("      , AnimateHeaderTitle = @AnimateHeaderTitle ");
		sql.AppendLine("      , MyTitle = CASE ");
		sql.AppendLine("                     WHEN MyTitle IS NULL OR MyTitle = '' THEN @MyTitle ");
		sql.AppendLine("                     ELSE MyTitle ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , Title_Ruby = @Title_Ruby ");
		sql.AppendLine("      , Company = @Company ");
		sql.AppendLine("      , Production = @Production ");
		sql.AppendLine("      , ThemeSongs = @ThemeSongs ");
		sql.AppendLine("      , Original = CASE ");
		sql.AppendLine("                     WHEN Original IS NULL OR Original = '' THEN @Original ");
		sql.AppendLine("                     ELSE Original ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , BroadcastText = @BroadcastText ");
		sql.AppendLine("      , Broadcast = @Broadcast ");
		sql.AppendLine("      , FirstBroadcast = @FirstBroadcast ");
		sql.AppendLine("      , ExportFileName = CASE ");
		sql.AppendLine("                     WHEN ExportFileName IS NULL OR ExportFileName = '' THEN @ExportFileName ");
		sql.AppendLine("                     ELSE ExportFileName ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , MetaTitleKana = CASE ");
		sql.AppendLine("                     WHEN MetaTitleKana IS NULL OR MetaTitleKana = '' THEN @MetaTitleKana ");
		sql.AppendLine("                     ELSE MetaTitleKana ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , MetaBroadcastKana = CASE ");
		sql.AppendLine("                     WHEN MetaBroadcastKana IS NULL OR MetaBroadcastKana = '' THEN @MetaBroadcastKana ");
		sql.AppendLine("                     ELSE MetaBroadcastKana ");
		sql.AppendLine("                   END ");
		sql.AppendLine("      , OfficialSiteUrl = @OfficialSiteUrl ");
		sql.AppendLine("      , OfficialPageTitle = @OfficialPageTitle ");
		sql.AppendLine("      , WikiUrl = @WikiUrl ");
		sql.AppendLine("      , DirectoryName = @DirectoryName ");
		sql.AppendLine("      , ContentHash = @ContentHash ");
		sql.AppendLine("      , IsExport = @IsExport ");
		sql.AppendLine("      , IsImport = @IsImport ");
		sql.AppendLine("      , HasXcf = @HasXcf ");
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
				HasXcf = work.HasXcf ? 1 : 0,
			},
			transaction);
		}

	/// <summary>
	/// 指定アニメ作品 ID に紐づくキャスト情報を全件削除します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="animeWorkId">削除対象の親アニメ作品 ID。</param>
	private async Task deleteCastsAsync(SQLiteConnection connection, DbTransaction transaction, int animeWorkId)
	{
		await connection.ExecuteAsync(
			" DELETE FROM Casts WHERE AnimeWorkId = @AnimeWorkId ",
			new { AnimeWorkId = animeWorkId },
			transaction);
	}

	/// <summary>
	/// 指定アニメ作品 ID に紐づくスタッフ情報を全件削除します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="animeWorkId">削除対象の親アニメ作品 ID。</param>
	private async Task deleteStaffsAsync(SQLiteConnection connection, DbTransaction transaction, int animeWorkId)
	{
		await connection.ExecuteAsync(
			" DELETE FROM Staffs WHERE AnimeWorkId = @AnimeWorkId ",
			new { AnimeWorkId = animeWorkId },
			transaction);
	}

	/// <summary>
	/// 指定 ID のアニメ作品レコードの UpdatedAt を現在日時に更新します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="id">更新対象レコードの ID。</param>
	private async Task touchWorkAsync(SQLiteConnection connection, DbTransaction transaction, int id)
	{
		await connection.ExecuteAsync(
			" UPDATE AnimeWorks SET UpdatedAt = DATETIME('now', 'localtime') WHERE Id = @Id ",
			new { Id = id },
			transaction);
	}

	/// <summary>
	/// 指定クールのアニメ作品を全件取得します。
	/// </summary>
	/// <param name="season">対象クール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>アニメ作品リスト。</returns>
	public async Task<List<AnimeWork>> GetBySeasonAsync(Season season, CancellationToken ct)
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
		sql.AppendLine("    , HasXcf ");
		sql.AppendLine("    , InsertedAt ");
		sql.AppendLine("    , UpdatedAt ");
		sql.AppendLine(" FROM AnimeWorks ");
		sql.AppendLine(" WHERE Year = @Year ");
		sql.AppendLine("   AND SeasonID = @SeasonID ");

		using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
		await connection.OpenAsync(ct);

		var rows = await connection.QueryAsync<AnimeWork>(
			sql.ToString(),
			new { Year = season.Year, SeasonID = (int)season.SeasonID });

		return rows.ToList();
	}

	/// <summary>
	/// 指定 ID のアニメ作品レコードの IsImport のみを更新します。
	/// IsImport はコンテンツハッシュの対象外のため、Skipped 時の差分更新に使用します。
	/// </summary>
	/// <param name="connection">SQLite 接続。</param>
	/// <param name="transaction">使用中のトランザクション。</param>
	/// <param name="id">更新対象レコードの ID。</param>
	/// <param name="isImport">更新後の IsImport 値（0 または 1）。</param>
	private async Task updateIsImportAsync(SQLiteConnection connection, DbTransaction transaction, int id, int isImport)
	{
		await connection.ExecuteAsync(
			" UPDATE AnimeWorks SET IsImport = @IsImport, UpdatedAt = DATETIME('now', 'localtime') WHERE Id = @Id ",
			new { Id = id, IsImport = isImport },
			transaction);
	}
}

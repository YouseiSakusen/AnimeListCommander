using System.Data.SQLite;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Contexts;

/// <summary>
/// データベースから情報を取得し <see cref="ApplicationContext"/> を構築するビルダーです。
/// </summary>
public class ApplicationContextBuilder
{
	/// <summary>
	/// SQLite 接続文字列。
	/// </summary>
	private readonly string connectionString;

	/// <summary>
	/// ロガー。
	/// </summary>
	private readonly ILogger<ApplicationContextBuilder> logger;

	/// <summary>
	/// 接続文字列とロガーを指定してインスタンスを初期化します。
	/// </summary>
	/// <param name="connectionString">SQLite 接続文字列。</param>
	/// <param name="logger">ロガー。</param>
	public ApplicationContextBuilder(string connectionString, ILogger<ApplicationContextBuilder> logger)
	{
		this.connectionString = connectionString;
		this.logger = logger;
	}

	/// <summary>
	/// データベースから各情報を取得し、<see cref="ApplicationContext"/> を非同期に構築します。
	/// </summary>
	/// <returns>構築された <see cref="ApplicationContext"/>。</returns>
	public async Task<ApplicationContext> BuildAsync()
	{
		using var connection = new SQLiteConnection(this.connectionString);
		await connection.OpenAsync();

		var seasons = await this.getSeasonsAsync(connection);
		var scrapingTargets = await this.getScrapingTargetsAsync(connection);
		var serviceConfigurations = await this.getServiceConfigurationsAsync(connection);
		var appConfiguration = await this.getAppConfigurationAsync(connection);

		var today = DateTime.Today;
		var currentSeason = seasons.FirstOrDefault(s =>
			s.Year == today.Year
			&& s.StartMonth <= today.Month
			&& today.Month <= s.EndMonth);

		return new ApplicationContext
		{
			Seasons = seasons,
			CurrentSeason = currentSeason,
			ScrapingTargets = scrapingTargets,
			ServiceConfigurations = serviceConfigurations,
			AppConfiguration = appConfiguration,
			ConnectionString = this.connectionString,
		};
	}

	/// <summary>
	/// Seasons テーブルと SeasonMasters テーブルを JOIN してクール一覧を取得します。
	/// </summary>
	private async Task<List<Season>> getSeasonsAsync(SQLiteConnection connection)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      s.Year ");
		sql.AppendLine("    , s.SeasonID ");
		sql.AppendLine("    , s.DisplayName ");
		sql.AppendLine("    , m.StartMonth ");
		sql.AppendLine("    , m.EndMonth ");
		sql.AppendLine("    , s.AnnictTermKey ");
		sql.AppendLine("    , s.MySiteSlug ");
		sql.AppendLine(" FROM Seasons s ");
		sql.AppendLine(" INNER JOIN SeasonMasters m ");
		sql.AppendLine("     ON s.SeasonID = m.SeasonID ");

		return (await connection.QueryAsync<Season>(sql.ToString())).ToList();
	}

	/// <summary>
	/// ScrapingTargets テーブルと SiteMasters テーブルを JOIN して偵察対象一覧を取得します。
	/// SQLite の IsPrimaryInput (INTEGER 0/1) は Dapper が bool へ自動変換します。
	/// </summary>
	private async Task<List<ScrapingTarget>> getScrapingTargetsAsync(SQLiteConnection connection)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      t.Year ");
		sql.AppendLine("    , t.SeasonID ");
		sql.AppendLine("    , t.SiteId ");
		sql.AppendLine("    , t.TargetUrl ");
		sql.AppendLine("    , m.SiteName ");
		sql.AppendLine("    , m.IsPrimaryInput ");
		sql.AppendLine(" FROM ScrapingTargets t ");
		sql.AppendLine(" INNER JOIN SiteMasters m ");
		sql.AppendLine("     ON t.SiteId = m.SiteId ");

		return (await connection.QueryAsync<ScrapingTarget>(sql.ToString())).ToList();
	}

	/// <summary>
	/// ServiceConfigurations テーブルから外部サービス設定一覧を取得します。
	/// SQLite の IsEnabled (INTEGER 0/1) は Dapper が bool へ自動変換します。
	/// </summary>
	private async Task<List<ServiceConfiguration>> getServiceConfigurationsAsync(SQLiteConnection connection)
	{
		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      SiteId ");
		sql.AppendLine("    , ServiceName ");
		sql.AppendLine("    , ApiKey ");
		sql.AppendLine("    , BaseUrl ");
		sql.AppendLine("    , IsEnabled ");
		sql.AppendLine(" FROM ServiceConfigurations ");

		return (await connection.QueryAsync<ServiceConfiguration>(sql.ToString())).ToList();
	}

	/// <summary>
	/// AppConfigurations テーブルからアプリケーション設定を取得します。
	/// </summary>
	private async Task<AppConfiguration> getAppConfigurationAsync(SQLiteConnection connection)
	{
		this.logger.ZLogInfo($"AppConfigurations テーブルからアプリ設定を取得します。");

		var sql = new StringBuilder();
		sql.AppendLine(" SELECT ");
		sql.AppendLine("      AnimeListRootPath ");
		sql.AppendLine("    , SummaryOutputPath ");
		sql.AppendLine("    , SummaryRetentionDays ");
		sql.AppendLine("    , SummaryMinKeepCount ");
		sql.AppendLine(" FROM AppConfigurations ");
		sql.AppendLine(" WHERE Id = 1 ");

		var result = await connection.QuerySingleAsync<AppConfiguration>(sql.ToString());
		this.logger.ZLogInfo($"アプリ設定を取得しました。AnimeListRootPath={result.AnimeListRootPath}");

		return result;
	}
}

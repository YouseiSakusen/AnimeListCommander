namespace AnimeListCommander.Contexts;

/// <summary>
/// アプリケーション全体で共有するコンテキスト情報を表します。
/// </summary>
public class ApplicationContext
{
	/// <summary>
	/// 全クール一覧を取得します。
	/// </summary>
	public required IReadOnlyList<Season> Seasons { get; init; }

	/// <summary>
	/// 現在のクールを取得または設定します。該当クールが存在しない場合は null となります。
	/// </summary>
	public Season? CurrentSeason { get; set; }

	/// <summary>
	/// 全偵察対象一覧を取得します。
	/// </summary>
	public required IReadOnlyList<ScrapingTarget> ScrapingTargets { get; init; }

	/// <summary>
	/// 全外部サービス設定一覧を取得します。
	/// </summary>
	public required IReadOnlyList<ServiceConfiguration> ServiceConfigurations { get; init; }

	/// <summary>
	/// アプリケーション設定を取得します。
	/// </summary>
	public required AppConfiguration AppConfiguration { get; init; }

	/// <summary>
	/// データベース接続文字列を取得します。
	/// </summary>
	public string ConnectionString { get; init; } = string.Empty;

	/// <summary>
	/// <see cref="CurrentSeason"/> に対応する偵察対象一覧を返します。
	/// <see cref="CurrentSeason"/> が null の場合は空のリストを返します。
	/// </summary>
	/// <returns>現在のクールに対応する <see cref="ScrapingTarget"/> の一覧。</returns>
	public IReadOnlyList<ScrapingTarget> GetCurrentSeasonTargets()
	{
		if (this.CurrentSeason is null)
		{
			return [];
		}

		return this.ScrapingTargets
			.Where(t => t.Year == this.CurrentSeason.Year && t.SeasonID == this.CurrentSeason.SeasonID)
			.ToList();
	}
}

namespace AnimeListCommander.Contexts;

/// <summary>
/// 偵察対象サイトとクールの組み合わせを表す不変エンティティです。
/// </summary>
public class ScrapingTarget
{
	/// <summary>
	/// 年度を取得します。
	/// </summary>
	public required int Year { get; init; }

	/// <summary>
	/// クール識別子を取得します。
	/// </summary>
	public required SeasonID SeasonID { get; init; }

	/// <summary>
	/// 偵察対象サイトを取得します。
	/// </summary>
	public required ScrapingSite SiteId { get; init; }

	/// <summary>
	/// 偵察対象 URL を取得します。
	/// </summary>
	public required string TargetUrl { get; init; }

	/// <summary>
	/// サイト名を取得します。
	/// </summary>
	public required string SiteName { get; init; }

	/// <summary>
	/// 主力入力サイトかどうかを取得します。
	/// SQLite の INTEGER (1:主力 / 0:補助) を bool にマッピングします。
	/// </summary>
	public required bool IsPrimaryInput { get; init; }
}

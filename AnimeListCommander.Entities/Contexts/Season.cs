namespace AnimeListCommander.Contexts;

/// <summary>
/// 放送クールを表す不変エンティティです。
/// </summary>
public class Season
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
	/// 表示名を取得します。
	/// </summary>
	public required string DisplayName { get; init; }

	/// <summary>
	/// クール開始月を取得します。
	/// </summary>
	public required int StartMonth { get; init; }

	/// <summary>
	/// クール終了月を取得します。
	/// </summary>
	public required int EndMonth { get; init; }

	/// <summary>
	/// Annict のタームキーを取得します。
	/// </summary>
	public required string AnnictTermKey { get; init; }

	/// <summary>
	/// 自サイト用のスラッグを取得します。
	/// </summary>
	public required string MySiteSlug { get; init; }

	/// <summary>
	/// クール開始日を取得します。
	/// </summary>
	public DateTime StartDay => new DateTime(this.Year, this.StartMonth, 1);
}

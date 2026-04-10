namespace AnimeListCommander.Intelligences;

/// <summary>
/// Annict API から取得した作品情報を保持する DTO です。
/// </summary>
public class AnnictWorkDto
{
	/// <summary>
	/// Annict 作品 ID を取得または設定します。
	/// </summary>
	public int AnnictId { get; set; }

	/// <summary>
	/// 作品名を取得または設定します。
	/// </summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>
	/// 英語表記（ローマ字）を取得または設定します。エクスポート時のファイル名等に使用します。
	/// </summary>
	public string TitleEn { get; set; } = string.Empty;

	/// <summary>
	/// タイトルかなを取得または設定します。
	/// </summary>
	public string TitleKana { get; set; } = string.Empty;

	/// <summary>
	/// Wiki の URL を取得または設定します。
	/// </summary>
	public string WikiUrl { get; set; } = string.Empty;

	/// <summary>
	/// メディア種別（tv, movie, ova 等）を取得または設定します。
	/// </summary>
	public string Media { get; set; } = string.Empty;
}

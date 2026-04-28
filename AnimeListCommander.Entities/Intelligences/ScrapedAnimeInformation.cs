namespace AnimeListCommander.Intelligences;

/// <summary>
/// スクレイピングで取得したアニメ情報を表すエンティティです。
/// </summary>
public class ScrapedAnimeInformation
{
	/// <summary>
	/// 作品名を取得または設定します。
	/// </summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>
	/// アニメイトタイムスのセクション見出し（h2）テキストを取得または設定します。
	/// 「再放送」等の注記はテーブル内ではなくこの見出し側に含まれます。
	/// </summary>
	public string AnimateHeaderTitle { get; set; } = string.Empty;

	/// <summary>
	/// キャスト情報を取得または設定します。
	/// </summary>
	public string Cast { get; set; } = string.Empty;

	/// <summary>
	/// スタッフ情報を取得または設定します。
	/// </summary>
	public string Staff { get; set; } = string.Empty;

	/// <summary>
	/// 主題歌情報を取得または設定します。
	/// </summary>
	public string ThemeSong { get; set; } = string.Empty;

	/// <summary>
	/// 公式サイト URL を取得または設定します。
	/// </summary>
	public string OfficialSiteUrl { get; set; } = string.Empty;

	/// <summary>
	/// kansou.me の「放送開始日」列のテキストを取得または設定します。
	/// </summary>
	public string KansouBroadcastStartDay { get; set; } = string.Empty;

	/// <summary>
	/// kansou.me の「放送局/放送日時」列の生テキストを取得または設定します。
	/// 行頭の「□」等の記号はトリム済みです。
	/// </summary>
	public string KansouBroadcastRawText { get; set; } = string.Empty;

	/// <summary>
	/// アニメイトタイムスの「スケジュール」行の生テキストを取得または設定します。
	/// </summary>
	public string AnimateScheduleRawText { get; set; } = string.Empty;

	/// <summary>
	/// アニメイトタイムスの「放送形態」行のテキストを取得または設定します。
	/// </summary>
	public string BroadcastType { get; set; } = string.Empty;
}

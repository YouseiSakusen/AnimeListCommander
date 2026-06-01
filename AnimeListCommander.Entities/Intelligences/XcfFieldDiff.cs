namespace AnimeListCommander.Intelligences;

/// <summary>
/// HasXcf=true 作品のスクレイピング差分情報を表します。
/// </summary>
public class XcfFieldDiff
{
	/// <summary>
	/// 差分が検出されたフィールド名を取得します。
	/// </summary>
	public string FieldName { get; init; } = string.Empty;

	/// <summary>
	/// DB に保存されている現在値を取得します。
	/// </summary>
	public string DbValue { get; init; } = string.Empty;

	/// <summary>
	/// スクレイピングで取得した値を取得します。
	/// </summary>
	public string ScrapedValue { get; init; } = string.Empty;
}

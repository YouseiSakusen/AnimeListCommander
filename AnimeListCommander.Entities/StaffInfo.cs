namespace AnimeListCommander;

/// <summary>
/// スタッフ情報を表します。
/// </summary>
public class StaffInfo
{
	/// <summary>
	/// 紐付くアニメ作品の主キーを取得します。
	/// </summary>
	public int AnimeWorkId { get; init; }

	/// <summary>
	/// 役職を取得または設定します。
	/// </summary>
	public string Role { get; set; } = string.Empty;

	/// <summary>
	/// 名前を取得または設定します。
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// 並び順を取得または設定します。
	/// </summary>
	public int SortOrder { get; set; }

	/// <summary>
	/// エクスポート対象かどうかを取得または設定します。
	/// </summary>
	public bool IsExport { get; set; } = true;
}

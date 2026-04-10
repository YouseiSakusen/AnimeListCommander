namespace AnimeListCommander.Operations;

/// <summary>
/// 作品設定ファイルの構成項目マスタを表します。
/// </summary>
public class WorkSettingItem
{
	/// <summary>
	/// ヘッダー名（#TITLE、#CAST など）を取得します。
	/// </summary>
	public string HeaderName { get; init; } = string.Empty;

	/// <summary>
	/// 上書き許可フラグを取得します。
	/// <c>false</c> の場合は条件付き（空なら上書き可）、<c>true</c> の場合は常に最新値で上書きします。
	/// </summary>
	public bool IsOverwriteAllowed { get; init; }

	/// <summary>
	/// 設定ファイル内での出力順を取得します。
	/// </summary>
	public int DisplayOrder { get; init; }

	/// <summary>
	/// 項目説明を取得します。
	/// </summary>
	public string? Description { get; init; }
}

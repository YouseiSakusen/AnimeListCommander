namespace AnimeListCommander.Contexts;

/// <summary>
/// 外部サービスの接続設定を表すエンティティです。
/// </summary>
public class ServiceConfiguration
{
	/// <summary>
	/// サイト識別子を取得または設定します。
	/// </summary>
	public int SiteId { get; set; } = 0;

	/// <summary>
	/// サービス名を取得または設定します。
	/// </summary>
	public string ServiceName { get; set; } = string.Empty;

	/// <summary>
	/// API キーを取得または設定します。
	/// </summary>
	public string ApiKey { get; set; } = string.Empty;

	/// <summary>
	/// ベース URL を取得または設定します。
	/// </summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>
	/// サービスが有効かどうかを取得または設定します。
	/// SQLite の INTEGER (1:有効 / 0:無効) を bool にマッピングします。
	/// </summary>
	public bool IsEnabled { get; set; } = true;
}

namespace AnimeListCommander.Intelligences;

/// <summary>
/// DB 保存処理の結果を表します。
/// </summary>
public class SaveResult
{
	/// <summary>
	/// 保存対象のアニメ作品情報を取得します。
	/// </summary>
	public AnimeWork Work { get; init; } = null!;

	/// <summary>
	/// 保存処理の結果状態を取得します。
	/// </summary>
	public SaveStatus Status { get; init; }

	/// <summary>
	/// 補足メッセージを取得します。失敗時のエラー情報などに使用します。
	/// </summary>
	public string? Message { get; init; }
}

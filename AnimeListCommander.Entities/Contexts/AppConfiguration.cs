namespace AnimeListCommander.Contexts;

/// <summary>
/// アプリケーション全体の設定を表す不変エンティティです。
/// </summary>
public class AppConfiguration
{
	/// <summary>
	/// アニメリストのルートパスを取得します。
	/// </summary>
	public required string AnimeListRootPath { get; init; }

	/// <summary>
	/// サマリー出力先パスを取得します。
	/// </summary>
	public required string SummaryOutputPath { get; init; }

	/// <summary>
	/// サマリー保持日数を取得します。
	/// </summary>
	public required int SummaryRetentionDays { get; init; }

	/// <summary>
	/// サマリーの最低保持件数を取得します。
	/// </summary>
	public required int SummaryMinKeepCount { get; init; }
}

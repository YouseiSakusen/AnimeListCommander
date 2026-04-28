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

	/// <summary>
	/// 指定クールのエクスポートパスを返します。
	/// </summary>
	/// <param name="season">対象クール。</param>
	/// <param name="fileName">ファイル名。省略時はクールのディレクトリパスを返します。</param>
	/// <returns>エクスポート先のフルパス。</returns>
	public string GetExportPath(Season season, string fileName = "")
	{
		var seasonDir = $"{season.Year}-{(int)season.SeasonID}";
		return string.IsNullOrEmpty(fileName)
			? Path.Combine(this.AnimeListRootPath, seasonDir)
			: Path.Combine(this.AnimeListRootPath, seasonDir, fileName);
	}
}

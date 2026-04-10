namespace AnimeListCommander;

/// <summary>
/// 放送クール識別子を表す列挙型です。
/// </summary>
public enum SeasonID
{
	/// <summary>
	/// 冬クール（1月-3月）。
	/// </summary>
	Winter = 1,

	/// <summary>
	/// 春クール（4月-6月）。
	/// </summary>
	Spring = 2,

	/// <summary>
	/// 夏クール（7月-9月）。
	/// </summary>
	Summer = 3,

	/// <summary>
	/// 秋クール（10月-12月）。
	/// </summary>
	Autumn = 4,
}

/// <summary>
/// DB 保存処理の結果状態を表す列挙型です。
/// </summary>
public enum SaveStatus
{
	/// <summary>
	/// 新規登録。
	/// </summary>
	New = 1,

	/// <summary>
	/// 内容更新・ハッシュ不一致。
	/// </summary>
	Updated = 2,

	/// <summary>
	/// 変更なし・ハッシュ一致。
	/// </summary>
	Skipped = 3,

	/// <summary>
	/// 処理失敗。
	/// </summary>
	Failed = 4,
}

/// <summary>
/// 偵察対象サイトを表す列挙型です。
/// </summary>
public enum ScrapingSite
{
	/// <summary>
	/// アニメイト。
	/// </summary>
	Animate = 1,

	/// <summary>
	/// アニメハック。
	/// </summary>
	AnimeHack = 2,

	/// <summary>
	/// 感想。
	/// </summary>
	Kansou = 3,

	/// <summary>
	/// Annict (APIによる情報補完)。
	/// </summary>
	Annict = 100,
}

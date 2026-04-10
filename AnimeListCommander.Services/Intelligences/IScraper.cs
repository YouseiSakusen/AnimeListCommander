using AnimeListCommander.Intelligences.Translators;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// アニメ情報をスクレイピングするスクレイパーのインターフェースです。
/// </summary>
public interface IScraper
{
	/// <summary>
	/// このスクレイパーに関連付けられたトランスレーターを取得または初期化します。
	/// </summary>
	ITranslator? Translator { get; init; }

	/// <summary>
	/// 指定した URL からアニメ情報を非同期に取得します。
	/// </summary>
	/// <param name="url">スクレイピング対象 URL。</param>
	/// <param name="ct">キャンセルトークン。</param>
	/// <returns>取得したアニメ情報のリスト。</returns>
	Task<List<ScrapedAnimeInformation>> ScrapeAsync(string url, CancellationToken ct = default);
}

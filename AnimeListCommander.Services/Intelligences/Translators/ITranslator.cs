using AnimeListCommander.Intelligences;

namespace AnimeListCommander.Intelligences.Translators;

/// <summary>
/// スクレイピング生データをアニメ作品情報に変換するトランスレーターのインターフェースです。
/// </summary>
public interface ITranslator
{
	/// <summary>
	/// スクレイピング生データを <see cref="AnimeWork"/> に変換します。
	/// </summary>
	/// <param name="rawData">スクレイピング生データ。</param>
	/// <returns>変換後のアニメ作品情報。</returns>
	AnimeWork Translate(ScrapedAnimeInformation rawData);

	/// <summary>
	/// 生データリストを変換し、既存のアニメ作品リストに統合して返します。
	/// </summary>
	/// <param name="rawDataList">スクレイピング生データのリスト。</param>
	/// <param name="currentList">統合先となる既存のアニメ作品リスト。</param>
	/// <returns>統合後のアニメ作品リスト。</returns>
	List<AnimeWork> TranslateMerge(List<ScrapedAnimeInformation> rawDataList, List<AnimeWork> currentList);
}

using AnimeListCommander.Helpers;
using HalationGhost.Utilities;

namespace AnimeListCommander.Intelligences.Translators;

/// <summary>
/// <see cref="ITranslator"/> の基底クラスです。
/// <see cref="Translate"/> の実行後、<see cref="AnimeWork.NormalizedTitle"/> を自動的に設定します。
/// 派生クラスは <see cref="translateCore"/> のみ実装してください。<see cref="AnimeWork.NormalizedTitle"/> の設定は不要です。
/// </summary>
public abstract class TranslatorBase : ITranslator
{
	/// <inheritdoc/>
	public AnimeWork Translate(ScrapedAnimeInformation rawData)
	{
		var work = this.translateCore(rawData);
		work.Title = JpStringConverter.ReplaceAsciiRomanNumerals(work.Title);
		work.NormalizedTitle = AnimeTitleNormalizer.Normalize(work.Title);
		if (string.IsNullOrWhiteSpace(work.MyTitle))
			work.MyTitle = JpStringConverter.ReplaceAsciiRomanNumerals(
				JpStringConverter.ToHalfWidthAlphanumeric(work.Title));
		this.applyCommonCleaning(work);
		return work;
	}

	/// <summary>
	/// サイト固有の変換ロジックを実装します。
	/// <see cref="AnimeWork.NormalizedTitle"/> の設定は <see cref="Translate"/> が自動で行うため、実装は不要です。
	/// </summary>
	/// <param name="rawData">スクレイピング済みの生データ。</param>
	/// <returns>変換後のアニメ作品情報。</returns>
	protected abstract AnimeWork translateCore(ScrapedAnimeInformation rawData);

	/// <inheritdoc/>
	public abstract List<AnimeWork> TranslateMerge(List<ScrapedAnimeInformation> rawDataList, List<AnimeWork> currentList);

	/// <summary>
	/// 全サイト共通のクレンジング処理を適用します。
	/// </summary>
	private void applyCommonCleaning(AnimeWork work)
	{
		work.Staffs.RemoveAll(s => s.Role == "脚本");

		foreach (var cast in work.Casts)
		{
			if (cast.Name == "ファイルーズあい")
				cast.Name = "ﾌｧｲﾙｰｽﾞあい";
		}
	}
}

using System.IO;
using AnimeListCommander.Contexts;
using AnimeListCommander.Helpers;
using AnimeListCommander.Masters;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Operations;

/// <summary>
/// 展開処理を統括するサービスです。
/// </summary>
public class OperationService
{
	private readonly OperationsRepository repository;
	private readonly MasterRepository masterRepository;
	private readonly ApplicationContext applicationContext;
	private readonly ILogger<OperationService> logger;

	/// <summary>
	/// <see cref="OperationService"/> の新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="repository">作戦リポジトリ。</param>
	/// <param name="masterRepository">共通マスタリポジトリ。</param>
	/// <param name="applicationContext">アプリケーションコンテキスト。</param>
	/// <param name="logger">ロガー。</param>
	public OperationService(
		OperationsRepository repository,
		MasterRepository masterRepository,
		ApplicationContext applicationContext,
		ILogger<OperationService> logger)
	{
		this.repository = repository;
		this.masterRepository = masterRepository;
		this.applicationContext = applicationContext;
		this.logger = logger;
	}

	/// <summary>
	/// 指定クールのアニメ作品を展開します。
	/// </summary>
	/// <param name="season">展開対象のクール。</param>
	/// <param name="ct">キャンセルトークン。</param>
	public async ValueTask DeployAsync(Season season, CancellationToken ct = default)
	{
		var works = await this.repository.GetAnimeWorksBySeasonAsync(season, ct);

		this.logger.ZLogInformation($"展開対象作品数: {works.Count} 件 ({season.Year}-{(int)season.SeasonID})");

		if (works.Count == 0)
		{
			return;
		}

		// 放送局マスタをメモリ上で適用（DB は書き換えない）
		var stationMasters = await this.masterRepository.GetBroadcastStationMastersAsync(ct);
		foreach (var work in works)
		{
			if (!string.IsNullOrEmpty(work.Broadcast) && stationMasters.TryGetValue(work.Broadcast, out var master))
			{
				work.Broadcast = master.OfficialName;
				work.MetaBroadcastKana = master.Kana;
			}
		}

		var masters = await this.repository.GetWorkSettingItemMastersAsync(ct);

		foreach (var work in works)
		{
			var directoryName = string.IsNullOrWhiteSpace(work.DirectoryName)
				? AnimeTitleNormalizer.ToSafeDirectoryName(work.MyTitle)
				: work.DirectoryName;
			var outputPath = Path.Combine(
					this.applicationContext.AppConfiguration.AnimeListRootPath,
					$"{season.Year}-{(int)season.SeasonID}",
					directoryName);

			this.logger.ZLogInformation($"[Deploy] {work.NormalizedTitle} => {outputPath}");

			await WorkSettingsHelper.WriteWorkSettingsAsync(outputPath, work, masters, this.logger);
		}
	}
}

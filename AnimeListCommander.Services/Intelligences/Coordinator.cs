using AnimeListCommander.Contexts;
using AnimeListCommander.Helpers;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Intelligences;

/// <summary>
/// スクレイピング前の前処理を統括するコーディネーターです。
/// </summary>
public class Coordinator
{
    private readonly ApplicationContext applicationContext;
    private readonly IntelligenceRepository repository;
    private readonly ILogger<Coordinator> logger;

    /// <summary>
    /// <see cref="Coordinator"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="applicationContext">アプリケーションコンテキスト。</param>
    /// <param name="repository">偵察リポジトリ。</param>
    /// <param name="logger">ロガー。</param>
    public Coordinator(ApplicationContext applicationContext, IntelligenceRepository repository, ILogger<Coordinator> logger)
    {
        this.applicationContext = applicationContext;
        this.repository = repository;
        this.logger = logger;
    }

    /// <summary>
    /// 指定クールの作品フォルダを巡回し、Work-Settings.txt の内容を DB に同期します。
    /// タイトル.xcf が存在するフォルダのみを対象とします。
    /// </summary>
    /// <param name="season">対象クール。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async ValueTask<Dictionary<string, AnimeWork>> CoordinateAsync(Season season, CancellationToken ct)
    {
        var rootPath = this.applicationContext.AppConfiguration.GetExportPath(season);

        if (!Directory.Exists(rootPath))
        {
            this.logger.ZLogWarning($"エクスポートフォルダが見つかりません。(path={rootPath})");
            return new Dictionary<string, AnimeWork>();
        }

        foreach (var dir in Directory.EnumerateDirectories(rootPath))
        {
            ct.ThrowIfCancellationRequested();

            var settingsPath = WorkSettingsHelper.GetSettingsPath(dir);
            if (!File.Exists(settingsPath))
                continue;

            Dictionary<string, List<string>> settings;
            try
            {
                settings = await WorkSettingsHelper.ParseWorkSettingsAsync(settingsPath);
            }
            catch (Exception ex)
            {
                this.logger.ZLogWarning($"Work-Settings.txt の読み込みに失敗しました。スキップします。(dir={dir}, ex={ex.Message})");
                continue;
            }

            var title = settings.TryGetValue("#TITLE", out var titleValues)
                ? titleValues.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(title))
                continue;

            var safeTitle = AnimeTitleNormalizer.ToSafeDirectoryName(title);
            var xcfPath = Path.Combine(dir, $"{safeTitle}.xcf");

            if (!File.Exists(xcfPath))
                continue;

            var animeWorkId = WorkSettingsHelper.GetAnimeWorkId(settings);
            if (animeWorkId is null)
            {
                this.logger.ZLogWarning($"[Coordinate] #AnimeWorkId が取得できませんでした。スキップします。(dir={dir})");
                continue;
            }

            var exportFileName = settings.TryGetValue("#EXPORT_FILENAME", out var exportValues)
                ? exportValues.FirstOrDefault()
                : null;

            var metaTitleKana = settings.TryGetValue("#META_TITLE_KANA", out var kanaValues)
                ? kanaValues.FirstOrDefault()
                : null;

            var metaBroadcastKana = settings.TryGetValue("#META_BROADCAST_KANA", out var broadcastKanaValues)
                ? broadcastKanaValues.FirstOrDefault()
                : null;

            var original = settings.TryGetValue("#ORIGINAL", out var originalValues)
                ? originalValues.FirstOrDefault()
                : null;

            var updated = await this.repository.UpdateFromWorkSettingsAsync(
                animeWorkId.Value,
                title,
                exportFileName,
                metaTitleKana,
                metaBroadcastKana,
                original,
                ct);

            if (updated > 0)
                this.logger.ZLogInformation($"[Coordinate] Work-Settings をDBに同期しました。(id={animeWorkId.Value})");
            else
                this.logger.ZLogWarning($"[Coordinate] 対象レコードが見つかりませんでした。(id={animeWorkId.Value})");
        }

        var works = await this.repository.GetBySeasonAsync(season, ct);

        var map = works
            .GroupBy(x => x.NormalizedTitle)
            .ToDictionary(g => g.Key, g => g.First());

        return map;
    }
}

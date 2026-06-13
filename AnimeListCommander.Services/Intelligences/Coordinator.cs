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

            var titleRuby = settings.TryGetValue("#TITLE_RUBY", out var rubyValues)
                ? rubyValues.FirstOrDefault()
                : null;

            var broadcastText = settings.TryGetValue("#BROADCAST_TEXT", out var broadcastTextValues)
                ? string.Join("\n", broadcastTextValues).TrimEnd()
                : null;

            var company = settings.TryGetValue("#COMPANY", out var companyValues)
                ? companyValues.FirstOrDefault()
                : null;

            var production = settings.TryGetValue("#PRODUCTION_LOGO", out var productionValues)
                ? productionValues.FirstOrDefault()
                : null;

            var themeSongs = settings.TryGetValue("#THEME_SONG", out var themeSongValues)
                ? string.Join("\n", themeSongValues).TrimEnd()
                : null;

            var firstBroadcast = settings.TryGetValue("#FIRST_BROADCAST", out var firstBroadcastValues)
                ? firstBroadcastValues.FirstOrDefault()
                : null;

            var broadcastLogo = settings.TryGetValue("#BROADCAST_LOGO", out var broadcastLogoValues)
                ? broadcastLogoValues.FirstOrDefault()
                : null;

            // #STAFF は Role行・Name行の2行1組
            var staffEntries = new List<(string Role, string Name)>();
            if (settings.TryGetValue("#STAFF", out var staffLines))
            {
                staffEntries = this.parseStaffEntries(staffLines);
            }

            var directoryName = AnimeTitleNormalizer.ToSafeDirectoryName(title);

            var updated = await this.repository.UpdateFromWorkSettingsWithXcfAsync(
                animeWorkId.Value,
                directoryName,
                title,
                titleRuby,
                exportFileName,
                metaTitleKana,
                metaBroadcastKana,
                original,
                broadcastText,
                broadcastLogo,
                company,
                production,
                themeSongs,
                firstBroadcast,
                staffEntries,
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

    /// <summary>
    /// #STAFF から読んだ行リストをスタッフエントリーに解析します。
    /// 以下の3形式に対応します：
    /// 1. 従来形式: 役職 / 名前（2行1組）
    /// 2. 特殊役職形式: 「キャラクターデザイン」直後に「総作画監督」が続く場合は結合（3行1組）
    /// 3. 1行形式: 「役職：名前」（全角コロン区切り）
    /// </summary>
    /// <param name="staffLines">Work-settings.txt から読んだ #STAFF の行リスト。</param>
    /// <returns>パースされたスタッフエントリーのリスト。</returns>
    private List<(string Role, string Name)> parseStaffEntries(List<string> staffLines)
    {
        var result = new List<(string Role, string Name)>();
        var i = 0;

        while (i < staffLines.Count)
        {
            var line = staffLines[i];

            // 空行は無視
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // 1行形式の判定（全角コロン「：」を含む）
            if (line.Contains('：'))
            {
                var parts = line.Split('：');
                if (parts.Length >= 2)
                {
                    var role = parts[0].Trim();
                    var name = parts[1].Trim();
                    if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
                    {
                        result.Add((role, name));
                    }
                }
                i++;
                continue;
            }

            // 2行以上の場合、複数行形式として処理
            if (i + 1 < staffLines.Count)
            {
                var nextLine = staffLines[i + 1];

                // 特殊役職形式の判定：現在行が「キャラクターデザイン」かつ次行が「総作画監督」
                if (line.Trim() == "キャラクターデザイン" && nextLine.Trim() == "総作画監督" && i + 2 < staffLines.Count)
                {
                    var name = staffLines[i + 2].Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var combinedRole = line.Trim() + "\n" + nextLine.Trim();
                        result.Add((combinedRole, name));
                    }
                    i += 3;
                    continue;
                }

                // 従来形式：2行1組（役職 / 名前）
                var role2Line = line.Trim();
                var name2Line = nextLine.Trim();
                if (!string.IsNullOrWhiteSpace(role2Line) && !string.IsNullOrWhiteSpace(name2Line))
                {
                    result.Add((role2Line, name2Line));
                }
                i += 2;
                continue;
            }

            // 最後の1行が残っている場合はスキップ
            i++;
        }

        return result;
    }
}

using System.Data.SQLite;
using AnimeListCommander.Contexts;
using Dapper;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace AnimeListCommander.Masters;

/// <summary>
/// 共通マスタデータの SQLite からの取得を担うリポジトリです。
/// </summary>
public class MasterRepository
{
    private readonly ApplicationContext applicationContext;
    private readonly ILogger<MasterRepository> logger;

    /// <summary>
    /// <see cref="MasterRepository"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="applicationContext">アプリケーションコンテキスト。</param>
    /// <param name="logger">ロガー。</param>
    public MasterRepository(ApplicationContext applicationContext, ILogger<MasterRepository> logger)
    {
        this.applicationContext = applicationContext;
        this.logger = logger;
    }

    /// <summary>
    /// BroadcastStationMasters テーブルから全レコードを取得し、ScrapedName をキーとする辞書を返します。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>ScrapedName をキーとする放送局マスタ辞書。</returns>
    public async Task<Dictionary<string, BroadcastStation>> GetBroadcastStationMastersAsync(CancellationToken ct)
    {
        using var connection = new SQLiteConnection(this.applicationContext.ConnectionString);
        await connection.OpenAsync(ct);

        var records = await connection.QueryAsync<BroadcastStation>(
            " SELECT ScrapedName, OfficialName, Kana FROM BroadcastStationMasters ");

        var result = records.ToDictionary(r => r.ScrapedName);
        this.logger.ZLogInfo($"BroadcastStationMasters 取得完了: {result.Count} 件");

        return result;
    }
}

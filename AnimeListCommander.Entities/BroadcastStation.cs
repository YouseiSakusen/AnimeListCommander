namespace AnimeListCommander;

/// <summary>
/// 放送局マスタ情報を表します。
/// </summary>
public class BroadcastStation
{
    /// <summary>
    /// スクレイピングで取得した生の放送局名を取得または設定します。
    /// </summary>
    public string ScrapedName { get; set; } = string.Empty;

    /// <summary>
    /// 展開時に使用する正式名称を取得または設定します。
    /// </summary>
    public string OfficialName { get; set; } = string.Empty;

    /// <summary>
    /// 読み仮名を取得または設定します。
    /// </summary>
    public string Kana { get; set; } = string.Empty;
}

using System.Security.Cryptography;
using System.Text;

namespace AnimeListCommander;

/// <summary>
/// アニメ作品情報を表します。GIMPマクロ設定ファイルの全項目に対応します。
/// </summary>
public class AnimeWork
{
	/// <summary>
	/// レコードの主キーを取得します。
	/// </summary>
	public int Id { get; init; }

	/// <summary>
	/// 放送年を取得します。
	/// </summary>
	public int Year { get; init; }

	/// <summary>
	/// 放送クール識別子を取得します。
	/// </summary>
	public SeasonID SeasonID { get; init; }

	/// <summary>
	/// 表示並び順インデックスを取得します。
	/// </summary>
	public int SortIndex { get; init; }

	/// <summary>
	/// アニメイトの生タイトルを取得または設定します。
	/// </summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>
	/// アニメイトタイムスのセクション見出し（h2）テキストを取得または設定します。
	/// 「再放送」等の注記はテーブル内ではなくこの見出し側に含まれます。
	/// </summary>
	public string AnimateHeaderTitle { get; set; } = string.Empty;

	/// <summary>
	/// 画像用タイトル（#TITLE）を取得または設定します。
	/// </summary>
	public string MyTitle { get; set; } = string.Empty;

	/// <summary>
	/// タイトルルビ（#TITLE_RUBY）を取得または設定します。
	/// </summary>
	public string Title_Ruby { get; set; } = string.Empty;

	/// <summary>
	/// キャスト一覧（#CAST）を取得または設定します。
	/// </summary>
	public List<CastInfo> Casts { get; set; } = new();

	/// <summary>
	/// スタッフ一覧（#STAFF）を取得または設定します。
	/// </summary>
	public List<StaffInfo> Staffs { get; set; } = new();

	/// <summary>
	/// 会社名（#COMPANY）を取得または設定します。
	/// </summary>
	public string Company { get; set; } = string.Empty;

	/// <summary>
	/// 制作会社名（#PRODUCTION_LOGO）を取得または設定します。
	/// </summary>
	public string Production { get; set; } = string.Empty;

	/// <summary>
	/// 主題歌（#THEME_SONG）を取得または設定します。
	/// </summary>
	public string ThemeSongs { get; set; } = string.Empty;

	/// <summary>
	/// 原作（#ORIGINAL）を取得または設定します。
	/// </summary>
	public string Original { get; set; } = string.Empty;

	/// <summary>
	/// 放送テキスト（#BROADCAST_TEXT）を取得または設定します。
	/// </summary>
	public string BroadcastText { get; set; } = string.Empty;

	/// <summary>
	/// 放送ロゴ（#BROADCAST_LOGO）を取得または設定します。
	/// </summary>
	public string Broadcast { get; set; } = string.Empty;

	/// <summary>
	/// 初回放送（#FIRST_BROADCAST）を取得または設定します。
	/// </summary>
	public string FirstBroadcast { get; set; } = string.Empty;

	/// <summary>
	/// エクスポートファイル名（#EXPORT_FILENAME）を取得または設定します。
	/// </summary>
	public string ExportFileName { get; set; } = string.Empty;

	/// <summary>
	/// メタタイトルかな（#META_TITLE_KANA）を取得または設定します。
	/// </summary>
	public string MetaTitleKana { get; set; } = string.Empty;

	/// <summary>
	/// メタ放送かな（#META_BROADCAST_KANA）を取得または設定します。
	/// </summary>
	public string MetaBroadcastKana { get; set; } = string.Empty;

	/// <summary>
	/// エクスポート対象かどうかを取得または設定します。
	/// </summary>
	public bool IsExport { get; set; } = true;

	/// <summary>
	/// 公式サイト URL を取得または設定します。
	/// </summary>
	public string OfficialSiteUrl { get; set; } = string.Empty;

	/// <summary>
	/// 公式サイトの &lt;title&gt; タグの内容を取得または設定します。
	/// </summary>
	public string OfficialPageTitle { get; set; } = string.Empty;

	/// <summary>
	/// インポート対象かどうかを取得または設定します。
	/// 再放送または公式サイト URL が未設定の場合は false になります。
	/// </summary>
	public bool IsImport { get; set; } = true;

	/// <summary>
	/// 突合（マッチング）用に正規化されたタイトルを取得または設定します。
	/// </summary>
	public string NormalizedTitle { get; set; } = string.Empty;

	/// <summary>
	/// Wiki の URL を取得または設定します。
	/// </summary>
	public string WikiUrl { get; set; } = string.Empty;

	/// <summary>
	/// 作品ディレクトリ名を取得します。
	/// </summary>
	public string DirectoryName { get; init; } = string.Empty;

	/// <summary>
	/// レコード内容のハッシュ値を取得します。変更検出に使用します。
	/// </summary>
	public string ContentHash { get; init; } = string.Empty;

	/// <summary>
	/// レコードの登録日時を取得します。
	/// </summary>
	public DateTime InsertedAt { get; init; }

	/// <summary>
	/// レコードの最終更新日時を取得します。
	/// </summary>
	public DateTime UpdatedAt { get; init; }

	/// <summary>
	/// コンテンツの変更検出に使用するハッシュ値を計算します。
	/// Title_Ruby は除外し、Casts・Staffs の子要素も含めて SHA256 で算出します。
	/// </summary>
	/// <returns>SHA256 ハッシュ値の16進数文字列（小文字）。</returns>
	public string CalculateContentHash()
	{
		var castNames = string.Join("|", this.Casts.Select(c => c.Name));
		var staffEntries = string.Join("|", this.Staffs.Select(s => $"{s.Role}:{s.Name}"));
		var raw = this.Title + this.MyTitle + this.Company + this.Production
			+ this.ThemeSongs + this.Original + this.BroadcastText
			+ this.Broadcast + this.FirstBroadcast + this.OfficialSiteUrl + this.OfficialPageTitle
			+ castNames + staffEntries;
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}
}

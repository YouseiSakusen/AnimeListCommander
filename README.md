# AnimeListCommander

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet.svg)](https://dotnet.microsoft.com/download)

## 📡 概要
**AnimeListCommander** は、個人サイト「[halation ghost](https://elf-mission.net/)」で公開しているアニメ一覧画像を、効率的に生成するための支援アプリケーションです。

外部サイトから取得したアニメの情報を正規化し、**GIMPマクロ（Batch Procedure）で画像を自動生成するための設定ファイル**を出力することを主目的としています。

## 🛠 主な機能
- **メタデータ統合管理**: 各種ソースから取得したアニメ情報を一括管理。
- **GIMPマクロ連携**: 画像生成用マクロが読み込むための設定ファイルを自動出力。
- **高度な正規化**: 28時間制パース、全角半角の揺らぎ吸収、出力ファイル名の小文字統一。
- **モダンな開発基盤**: .NET 10 を採用。
- **コーディング手法**: Geminiと相談しつつ仕様を決定し、プロンプトを出力してもらい、実装はClaude Sonnet4.6に99%丸投げして作成しました。

## 🏗 アーキテクチャ
プロジェクト分割により、役割の分離と名前空間の衝突回避を両立させた構成を採用しています。

- **Framework**: .NET 10 (WPF)
- **Architecture**: Generic Host による DI コンテナ活用
- **UI Library**: WpfUi
- **Project Structure**:
  - `Intelligences`: 外部情報の収集・解析（偵察）を担当。
  - `Operations`: UIおよびアプリケーションの動作（展開）を制御。

## 🚀 セットアップ (開発中)
現在はソースコードのみの公開です。

### データベースについて
- データの保存には SQLite を使用しています。
- セットアップの簡略化のため、**起動時にテーブルを自動生成する初期化ロジック**への移行を計画中です。

## 📜 ライセンス
[Apache License 2.0](LICENSE)

---
Developed by **YouseiSakusen**

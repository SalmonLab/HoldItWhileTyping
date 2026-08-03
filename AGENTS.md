# HoldItWhileTyping 開発用 AGENTS（Windows 専用）

このリポジトリは Windows 向け常駐トレイユーティリティ `HoldItWhileTyping` の実装リポジトリです。  
目的は、入力中に別ウィンドウが勝手にアクティブ化される現象を抑え、作業の集中を維持することです。

## 優先順位
1. 安全性（入力監視・終了手段・権限）
2. 動作の確実性（常駐状態、入力検知、フォーカス復帰）
3. 設定の再現性（保存/再読込）
4. 変更の局所化

## 文字コード/改行/報告運用
- このリポジトリ内のテキストファイルは原則 UTF-8（必要なら BOM なし）、CRLF を維持する。
- 人への報告は Shift_JIS で文字化けしにくい表現を優先する（記号を過剰に増やさない）。
- 既存運用と衝突する場合はこのリポジトリ方針を優先する。

## 変更方針
- 変更は本体機能（フック、設定、トレイ、起動設定）に限定する。
- 不要なリファクタリングやスタイル全面刷新は避ける。
- 設定キー名・JSON 形式は後方互換を重視し、削除や破壊を避ける。

## 開発・検証ルール
- `AGENTS.md` を編集した場合は、当該変更に対して**必ず**以下を実施すること。
  1. 実装変更
  2. 変更内容の検証（影響範囲の確認）
  3. `dotnet build`
  4. `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
  5. 生成 `HoldItWhileTyping.exe` の存在確認
- 実装前にこの `AGENTS.md` と `README.md` を確認する。
- 依存は .NET 標準 API を優先し、不要な外部依存を追加しない。
- Windows 固有の挙動は対象 OS 上で確認する。
- UI 変更時はトレイの主要項目（有効/無効、ロック時間、除外アプリ、透明モード）が表示・保存されることを確認する。

## アーキテクチャ上の前提
- 対象フレームワーク: `.NET 8.0-windows`
- 主構成: WinForms (`OutputType=WinExe`)
- 入力監視: 低レベルキーボード・マウス
- 設定保存: `%LocalAppData%\HoldItWhileTyping\settings.json`
- 常駐トレイ UI: `NotifyIcon` と関連コンテキストメニュー
- 配布: `HoldItWhileTyping.exe`（Single File）

## 実装時の禁止事項
- コンソールを起動しない（WinExe のトレイ常駐を維持）。
- 既存コマンドを削除して既定動作を壊す変更を避ける。
- 自動起動レジストリの書き換えは、明示的要求がない限り追加しない。
- 低レベル処理と保存処理は責務を分離して可読性を保つ。

## リリース・配布ルール
- 配布は原則 `self-contained` 単体実行で作成する。
- 署名、インストーラ、更新機構の追加は別タスクで計画する。

## 例外ケース
- OS によってはフォーカス制御 API が想定どおり動作しないことがあるため、再試行や安全停止を過度に行わず実運用に合わせる。

## 変更完了条件
- `dotnet build` が成功していること
- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` が成功していること
- `bin\\Release\\net8.0-windows\\win-x64\\publish\\HoldItWhileTyping.exe` が存在すること
- このファイル内に実施結果を記録していること

## 既知の検証結果テンプレート（必要なら追記）
- 変更内容:
- 検証コマンド:
- 実行日:
- 問題/例外:

## 検証記録
- 変更内容: `FocusGuardService` の除外アプリ/透明モード関連実装の文法不備を修正し、`build` と `publish` を実施して完了条件を確認
- 検証コマンド:
  - `dotnet build`
  - `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
- 実行日: 2026-08-03
- 問題/例外: 初回ビルド時、`Program.cs` の文字列補間式で `}` の不足による構文エラーを1件修正

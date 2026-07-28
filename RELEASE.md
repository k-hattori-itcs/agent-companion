# リリース手順

この手順は、利用者向けのGitHub Releaseを公開するときに必ず実施します。通常のCI成果物は配布物ではありません。

## 自動で強制される条件

Release workflowは、次を満たさない限りGitHub Releaseを作成しません。

- `main` から到達できるコミットを指す、注釈付き `vX.Y.Z` タグであること
- タグ名が `AgentCompanion.csproj` の `VersionPrefix` と一致すること
- `CHANGELOG.md` に同じバージョンの見出しがあること
- restore、書式、文字コード、資料アセット、build、test、依存脆弱性検査、配布ZIP検査がすべて成功すること
- Release ZIPと `SHA256SUMS.txt` にGitHub Attestationを付与できること

`main` はGitHub側でもPR経由、`build-windows` 成功、会話解決、線形履歴、force push・削除禁止を強制しています。

## 公開前

1. 機能変更は `main` から作成したブランチで実装します。
2. `AgentCompanion.csproj` の `VersionPrefix` を次の公開バージョンへ更新します。
3. `CHANGELOG.md` に同じバージョンの公開内容を追記します。
4. PRでレビューを行い、`build-windows` の成功と会話解決を確認します。
5. PRを `main` にマージします。`main` へ直接コミットしません。

## 公開

1. `main` の最新状態を取得し、作業ツリーが空であることを確認します。

   ```powershell
   git switch main
   git pull --ff-only origin main
   git status --short
   ```

2. バージョンに一致する**注釈付きタグ**を作成してpushします。

   ```powershell
   git tag -a vX.Y.Z -m "AgentCompanion vX.Y.Z"
   git push origin vX.Y.Z
   ```

3. GitHub Actionsの `release` workflowが成功するまで待ちます。失敗したタグは公開済み成果物として扱いません。
4. GitHub ReleaseにZIPと `SHA256SUMS.txt` が添付され、Attestationが作成されていることを確認します。

## 公開後

1. ReleaseからZIPをダウンロードします。
2. `SHA256SUMS.txt` とZIPのSHA-256を照合します。
3. GitHub CLIが使える場合は、以下でビルド来歴を検証します。

   ```powershell
   gh attestation verify .\AgentCompanion-vX.Y.Z-win-x64.zip --repo k-hattori-itcs/agent-companion
   ```

4. Windows 10 / 11の別環境で展開、起動、終了、自動起動、更新、アンインストールを確認します。

## 例外対応

公開後に不具合が見つかった場合、Release ZIPを直接差し替えません。修正PRを作成し、新しいバージョンと新しい注釈付きタグでリリースします。WindowsのAuthenticode署名は付与していないため、発行元確認の警告が出る場合があることは、配布時に明示します。
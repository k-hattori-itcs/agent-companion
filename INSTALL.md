# AgentCompanion 導入・更新手順

## 対象環境

- Windows 10 / 11（64 bit）
- 管理者権限は不要です。
- 配布 ZIP は自己完結型です。.NET Runtime を別途導入する必要はありません。
- EXE は Authenticode 署名されていません。Windows が発行元を確認できない警告を表示する場合があります。

## 導入

1. [Releases](https://github.com/k-hattori-itcs/agent-companion/releases) から `AgentCompanion-vX.Y.Z-win-x64.zip` と `SHA256SUMS.txt` をダウンロードします。
2. ZIP を任意のユーザー用フォルダへ展開します。例: `%LOCALAPPDATA%\Programs\AgentCompanion`
3. ZIP の SHA-256 が `SHA256SUMS.txt` の同名ファイルの値と一致することを確認します。

   ```powershell
   Get-FileHash "$env:USERPROFILE\Downloads\AgentCompanion-vX.Y.Z-win-x64.zip" -Algorithm SHA256
   ```

4. GitHub CLI を使える場合は、Release ZIP のビルド来歴も確認します。

   ```powershell
   gh attestation verify "$env:USERPROFILE\Downloads\AgentCompanion-vX.Y.Z-win-x64.zip" --repo k-hattori-itcs/agent-companion
   ```

   この検証は、ZIP がこのリポジトリのGitHub Actionsでビルドされたことを確認します。Windowsの発行元証明ではありません。

5. `AgentCompanion.exe` を起動します。
6. 初回起動後、タスクトレイの AgentCompanion アイコンを右クリックして `設定` を開き、監視対象を設定します。
7. 自動起動が必要な場合は、設定画面の `接続` タブで `Windows起動時にこの AgentCompanion を起動する` を有効にします。

アプリ本体は終了しても設定、ログ、キャラクターデータを `%LOCALAPPDATA%\AgentCompanion\instances\<instance-id>` に保持します。設定画面からインポートした追加キャラクターもこのフォルダに保存されます。

## 更新

1. タスクトレイの AgentCompanion アイコンを右クリックし、`終了` を選びます。
2. 新しいReleaseの ZIP と `SHA256SUMS.txt` をダウンロードし、ハッシュを確認します。GitHub CLIを使える場合は、更新前に必ずGitHub Attestationも検証してください。ハッシュだけでは配布元の真正性を確認できません。

   ```powershell
   gh attestation verify "$env:USERPROFILE\Downloads\AgentCompanion-vX.Y.Z-win-x64.zip" --repo k-hattori-itcs/agent-companion
   ```
3. **初回に展開した同じフォルダ**で、更新スクリプトを実行します。

   ```powershell
   Unblock-File .\Update-AgentCompanion.ps1
   .\Update-AgentCompanion.ps1 `
     -ArchivePath "$env:USERPROFILE\Downloads\AgentCompanion-vX.Y.Z-win-x64.zip" `
     -ChecksumPath "$env:USERPROFILE\Downloads\SHA256SUMS.txt" `
     -Restart
   ```

更新スクリプトはハッシュとZIP内容を確認してから差し替えます。旧EXEは `AgentCompanion.exe.previous` として1世代保持します。アプリフォルダのパスで設定プロファイルを分けるため、更新時は同じフォルダを使ってください。**同じフォルダで更新する限り、設定画面から追加したキャラクターは保持されます。** 別のフォルダへ展開し直すと別のプロファイルになるため、追加キャラクターは自動移行されません。

## Claude 利用量API

Claude Code の利用量を正確に表示するための API は、初期状態では無効です。使用する場合は、設定画面の `接続` タブにある `Claude Code OAuthで利用量を取得する` を有効にしてください。

有効にすると、Claude ホームの `.credentials.json` から OAuth アクセストークン・更新トークン・有効期限を読み取り、Anthropic の利用状況 API に問い合わせます。期限切れまたは期限が近い場合は、更新トークンでアクセストークンを更新します。AgentCompanionはトークンを自分の設定へ保存、表示、ログ出力しませんが、更新成功時はClaude Codeと共有する `.credentials.json` の値を原子的に更新します。429応答の待機期限は再起動後も保持します。無効のままでも、Claude Code のローカル履歴および statusline 情報から状況や利用量を表示できます。

## アンインストール

1. タスクトレイの AgentCompanion アイコンを右クリックし、`終了` を選びます。
2. インストールフォルダでアンインストールスクリプトを実行します。

   ```powershell
   Unblock-File .\Uninstall-AgentCompanion.ps1
   .\Uninstall-AgentCompanion.ps1
   ```

3. 設定、ログ、インポート済みキャラクターも削除する場合だけ、`-RemoveData` を指定します。通常のアンインストールではこれらのデータは保持されます。

   ```powershell
   .\Uninstall-AgentCompanion.ps1 -RemoveData
   ```

4. スクリプト終了後、インストールフォルダを削除します。

この手順で、現在のインストール先に対応するスタートアップ登録を削除します。
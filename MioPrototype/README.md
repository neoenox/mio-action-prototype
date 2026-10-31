# MioPrototype — 神楽澪 戦闘プロトタイプ

Unity 6000.4.5f1 / Built-in Render Pipeline のベルトアクション試作です。
主人公には VRM モデルを使用し、敵はプリミティブモデルまたは外部 VRM を使用します。

## 遊び方

実行版の起動方法、操作、ゲームの流れは [親フォルダーの README](../README.md) を参照してください。

## 開発

Unity でこのフォルダーを開き、`Assets/Scenes/Prototype.unity` を使用します。
Windows 実行版は Unity の `Ryona > Build Windows Player` から生成できます。

戦闘ルールは `Assets/RyonaCore/Runtime`、ゲーム内の制御と表示は
`Assets/RyonaGame/Runtime` にあります。Unity Test Runner で EditMode と
PlayMode のテストを実行できます。

# 被弾ボイスのバックアップ

2026-11-05 に被弾ボイスを差し替えたときの、**差し替え前の音**です。
戻したくなったらここからコピーして戻します。

| ファイル | 使われていた場面 |
|---|---|
| `hurt_u.wav` | 軽い被弾（`SfxId.HurtU`） |
| `hurt_ku.wav` | 重い被弾・腹パン（`SfxId.HurtKu`） |

## いまの音

| ファイル | 場面 | 元のファイル |
|---|---|---|
| `MioPrototype/Assets/RyonaGame/Resources/Voice/hurt_small.wav` | 軽い被弾 | `uu.wav` |
| `MioPrototype/Assets/RyonaGame/Resources/Voice/hurt_large.wav` | 重い被弾 | `aa.wav` |
| `MioPrototype/Assets/RyonaGame/Resources/Voice/hurt_belly.wav` | 腹への一撃 | `d_ofu.wav` |

`hurt_u.wav` / `hurt_ku.wav` は `Assets/RyonaGame/Resources/Voice/` にも残してあります（未使用）。

**このフォルダの `.wav` は git に入りません**（`Assets/.../Voice/` と同じく、配布元の
ライセンスが未確認のため）。控えはこのマシンのローカルにだけ残ります。消さないでください。

## 戻し方

1. `hurt_u.wav` / `hurt_ku.wav` を `MioPrototype/Assets/RyonaGame/Resources/Voice/` へコピー
2. `ProceduralSfx.cs` の `LoadVoice` を元のパスに戻す
3. `CombatFeel.cs` の `PlayHurtVoice` の割り当てを戻す（腹は `HurtKu` を共用していた）

このフォルダは Unity の `Assets` の外なので、インポートされません。

# 後ろ拘束中の腹パン演出 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 後ろ拘束中に別の敵の腹パンチが当たったら、「く」の字の被弾姿勢と腹・顔が入るカメラ寄せを再生し、演出後に拘束を解放する。

**Architecture:** `HeldBellyPunchDirector`（Feel 層）が拘束開始・被弾・解放を調停する。`PlayerGrapple` は後ろ拘束の時間切れを廃止してもがき抑制APIを持ち、`CharacterAnimator` は Grabbed 状態の上に被弾オーバーレイを重ね、`EnemyBrain` はタッグ技用の腹パンチを開始する。カメラは既存の `ThirdPersonCamera.PushDistance/PushPivot` を使う。

**Tech Stack:** Unity 6000.4.5f1 / C# / NUnit PlayMode tests / Unity batchmode

---

## Task 1: 後ろ拘束を時間切れなしにし、もがき抑制を追加

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.cs`
- Test: `MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs`（新規・このタスクで雛形）

**Step 1: 失敗するテストを書く**

`HeldBellyPunchTests.cs` に、後ろ拘束が 3 秒を超えても解けないことを書く。

```csharp
[UnityTest]
public IEnumerator 後ろ拘束は時間切れでは解けない()
{
    yield return TestScene.LoadPrototype();
    var player = Object.FindFirstObjectByType<PlayerStats>();
    var grapple = player.GetComponent<PlayerGrapple>();
    var captor = Object.FindFirstObjectByType<EnemyBrain>();
    grapple.Begin(captor, GrappleKind.Back);

    yield return new WaitForSeconds(3.4f);

    Assert.IsTrue(grapple.IsGrappled, "Back hold must not time out");
    Assert.IsFalse(grapple.HasTimeLimit, "Back hold has no time limit");
}
```

**Step 2: 失敗を確認**

Unity Test Runner（PlayMode）で `HeldBellyPunchTests` を実行。
Expected: FAIL（`HasTimeLimit` が無い、または 3 秒で `Fail()` する）。

**Step 3: 最小実装**

`PlayerGrapple` に以下を追加する。

```csharp
float _heldSeconds;
float _suppressRemaining;

public bool HasTimeLimit => Kind == GrappleKind.Pin;
public float HeldSeconds => _heldSeconds;
public bool IsStruggleSuppressed => _suppressRemaining > 0f;
public float SuppressRemaining => Mathf.Max(0f, _suppressRemaining);

public float Remaining =>
    HasTimeLimit ? Mathf.Max(0f, _remaining) : 0f;

public void SuppressStruggle(float seconds)
{
    if (seconds > 0f)
        _suppressRemaining = Mathf.Max(_suppressRemaining, seconds);
}
```

`Update()` を次の形にする。

```csharp
_heldSeconds += Time.deltaTime;

if (_suppressRemaining > 0f)
{
    _suppressRemaining = Mathf.Max(0f, _suppressRemaining - Time.deltaTime);
    StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 6f);
    return;
}

if (HasTimeLimit)
{
    _remaining -= Time.deltaTime;
    if (_remaining <= 0f)
    {
        Fail();
        return;
    }
}

if (InputReader.StrugglePressed())
{
    StrugglePulse = 1f;
    if (_resistance.RegisterInput())
    {
        Escape();
        return;
    }
}
```

`Begin()` で `_heldSeconds = 0f; _suppressRemaining = 0f;` とし、
`_remaining = kind == GrappleKind.Pin ? CombatConstants.GrappleLimitSeconds : float.PositiveInfinity;`。
`Release()` / `Escape()` / `Fail()` でも `_suppressRemaining = 0f;` に戻す。

**Step 4: テストを再実行**

Expected: PASS。既存の EditMode 19 件も PASS。

**Step 5: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/PlayerGrapple.cs \
        MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs
git commit -m "feat: 後ろ拘束を時間切れなしにし、もがき抑制を追加"
```

---

## Task 2: Grabbed 中の「く」の字オーバーレイ

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.cs`

**Step 1: 失敗するテストを書く**

`HeldBellyPunchTests` に追加する。

```csharp
[UnityTest]
public IEnumerator 腹パンでくの字になりもがきが止まる()
{
    yield return LoadCharacter();
    _anim.PlayHeldBellyPunch(0.55f);
    Assert.IsTrue(_anim.IsHeldBellyPunch);
    yield return new WaitForSeconds(0.15f);
    Assert.Greater(_anim.HeldBellyPunchFold, 0.5f);
}
```

`LoadCharacter()` は既存の `HurtPoseRuntimeTests` と同じ手順で
`PlayerStats` → `CharacterAnimator` を取得し、`Rig.Ready` まで待つ。

**Step 2: 失敗を確認**

Expected: FAIL（`PlayHeldBellyPunch` が無い）。

**Step 3: 最小実装**

`CharacterAnimator` に追加する。

```csharp
float _heldBellyPunchRemaining;
float _heldBellyPunchTotal;

public bool IsHeldBellyPunch => _heldBellyPunchRemaining > 0f;

public void PlayHeldBellyPunch(float seconds)
{
    if (seconds <= 0f)
        return;

    _heldBellyPunchTotal = seconds;
    _heldBellyPunchRemaining = seconds;
}

public float HeldBellyPunchFold
{
    get
    {
        if (_heldBellyPunchTotal <= 0f || _heldBellyPunchRemaining <= 0f)
            return 0f;

        float t = _heldBellyPunchTotal - _heldBellyPunchRemaining;
        float inT = Mathf.SmoothStep(0f, 1f, t / 0.07f);
        float outT = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.37f) / 0.18f);
        return Mathf.Clamp01(inT * outT);
    }
}
```

`LateUpdate()` の先頭で `_heldBellyPunchRemaining = Mathf.Max(0f, _heldBellyPunchRemaining - Time.deltaTime);`。

`State.Grabbed` の `if (Grapple.Kind == GrappleKind.Back)` ブロックの
`break;` の直前に、次のオーバーレイを入れる。

```csharp
float fold = HeldBellyPunchFold;
if (fold > 0f)
{
    // もがきの揺れを止める
    strain = 0f;
    claw = 0f;

    torsoPitch = Mathf.Lerp(torsoPitch, 40f, fold);
    headPitch = Mathf.Lerp(headPitch, -8f, fold);
    torsoYaw = Mathf.Lerp(torsoYaw, 0f, fold);
    headYaw = Mathf.Lerp(headYaw, 0f, fold);

    // 腰を後ろへ引き、膝を曲げて「く」の字にする
    hipsOffset.y = Mathf.Lerp(hipsOffset.y, StanceHipsY - 0.10f, fold);
    hipsOffset.z = Mathf.Lerp(hipsOffset.z, StanceHipsZ - 0.09f, fold);
}
```

**Step 4: テストを再実行**

Expected: PASS。

**Step 5: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/Rig/CharacterAnimator.cs \
        MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs
git commit -m "feat: 後ろ拘束中に腹パンのくの字オーバーレイを追加"
```

---

## Task 3: タッグ技用の腹パンチを敵に指示できるようにする

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.cs`

**Step 1: 実装**

`EnemyBrain` に追加する。

```csharp
/// <summary>
/// 拘束中のプレイヤーへ、Body 狙いのジャブを開始する（タッグ技用）。
/// 正面・間合い内・攻撃トークンありのときだけ開始する。
/// </summary>
public bool TryStartBellyPunch()
{
    if (Phase == EnemyPhase.Dead || Phase == EnemyPhase.Windup ||
        Phase == EnemyPhase.Attack || Phase == EnemyPhase.Recover ||
        Phase == EnemyPhase.Stagger || IsRising)
        return false;

    if (Player == null || Player.IsDead)
        return false;

    var grapple = Player.GetComponent<PlayerGrapple>();
    if (grapple == null || !grapple.IsGrappled || grapple.Captor == this)
        return false;

    Vector3 to = Player.transform.position - transform.position;
    to.y = 0f;
    float distance = to.magnitude;
    if (distance > RangeOf(AttackId.Jab) + 0.25f)
        return false;

    Vector3 facing = transform.forward;
    facing.y = 0f;
    if (to.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
        Vector3.Dot(facing.normalized, to.normalized) < 0.85f)
        return false;

    if (Group != null && !Group.TryAcquireAttackToken(this))
        return false;

    ForceZone = PunchZone.Body;
    BeginWindup(AttackId.Jab);
    ForceZone = null;

    Debug.Log($"[HeldPunch] order {name} zone=Body dist={distance:F2}");
    return true;
}
```

**Step 2: コンパイル確認**

Unity でスクリプトコンパイルエラーが無いこと。
PlayMode 既存テストが通ること（この時点では演出はまだ無い）。

**Step 3: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/EnemyBrain.cs
git commit -m "feat: 拘束中のプレイヤーへ腹パンチを指示するAPIを追加"
```

---

## Task 4: HeldBellyPunchDirector

**Files:**
- Create: `MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.cs`

**Step 1: 実装**

新規コンポーネント。`RuntimeInitializeOnLoadMethod` で自動追加する。
状態は `Idle → Waiting → Performing → Cooldown`。

主要フィールド:

```csharp
public float PunchDelaySeconds = 1.6f;
public float RetrySeconds = 1.0f;
public float PerformanceSeconds = 0.55f;
public float CameraDistanceScale = 0.48f;
public float CameraPivotOffset = 0.25f;
public float ReleaseGraceSeconds = 0.6f;
public int MaxAttempts = 3;
```

公開API:

```csharp
public bool IsPerforming { get; private set; }
public float NextPunchRemaining { get; }
public bool HasAttacker { get; private set; }
public event Action Performed;
```

処理:

1. `Start` で `PlayerGrapple` / `PlayerStats` / `CharacterAnimator` / `ThirdPersonCamera` を解決。
2. `PlayerGrapple.Started` で `Kind == Back` なら `Waiting` に入り、`_timer = PunchDelaySeconds`。
3. `Update` で `IsGrappled` を確認。切れていたらリセット。
4. `Waiting` 中に `_timer <= 0` なら `TryOrderPunch()`。
5. `TryOrderPunch()`:
   - 拘束者以外・生存・立ち上がり中でない最寄りの敵を選ぶ
   - `player.position + player.forward * 1.32f` に置き、プレイヤーへ向ける
   - `enemy.TryStartBellyPunch()` を呼ぶ
   - 成功：`HasAttacker = true`、`_waitingForHit = true`
   - 失敗：`_timer = 0.3f` で再試行。`MaxAttempts` を超えたら `RetrySeconds` 後に再予約
6. `PlayerStats.HitReceived` で `ShouldPerform(result)` を判定し、`BeginPerformance()`。
7. `BeginPerformance()`:
   - `IsPerforming = true; _performanceRemaining = PerformanceSeconds;`
   - `_grapple.SuppressStruggle(PerformanceSeconds);`
   - `_anim.PlayHeldBellyPunch(PerformanceSeconds);`
   - `_camera.PushDistance(CameraDistanceScale, PerformanceSeconds);`
   - `_camera.PushPivot(CameraPivotOffset, PerformanceSeconds);`
   - 他の敵へ `HoldFire(PerformanceSeconds + 0.2f);`
   - `Performed?.Invoke();`
   - `Debug.Log("[HeldPunch] trigger ...");`
8. `Update` で `_performanceRemaining <= 0` なら `FinishPerformance()`。
9. `FinishPerformance()`:
   - まだ掴まれていれば `_grapple.Release();`
   - 全敵へ `HoldFire(ReleaseGraceSeconds);`
   - `IsPerforming = false;` `Cooldown = 0.6f;`
   - `Debug.Log("[HeldPunch] release");`
10. `ShouldPerform(result)`:
    - `_grapple.IsGrappled && Kind == Back`
    - `_stats.LastHitZone == PunchZone.Body`
    - `result.Damage > 0 && !result.Blocked`
    - `_stats.LastAttacker != null && _stats.LastAttacker != _grapple.Captor.transform`
    - 攻撃者が前方（`dot(forward, attacker - player) > 0.1`）
    - `!_performing && _cooldown <= 0 && !_stats.IsDead && Time.timeScale > 0`

**Step 2: コンパイル確認**

**Step 3: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/Feel/HeldBellyPunchDirector.cs
git commit -m "feat: 後ろ拘束中の腹パン演出ディレクターを追加"
```

---

## Task 5: HUD の残り時間を「腹パンまで」に変更

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Runtime/MatchHud.cs`

**Step 1: 実装**

`UpdateGrapple()` の文字列を分岐する。

```csharp
string where = _grapple.Kind == GrappleKind.Back ? "（後ろから）" : string.Empty;
string tail;

if (_grapple.HasTimeLimit)
{
    tail = $"あと {_grapple.Remaining:F1}s";
}
else
{
    var director = FindFirstObjectByType<HeldBellyPunchDirector>();
    if (director != null && director.IsPerforming)
        tail = "腹パン！";
    else if (director != null && director.HasAttacker)
        tail = $"腹パンまで {director.NextPunchRemaining:F1}s";
    else
        tail = "別の敵なし";
}

_grappleText.text = $"掴まれている{where} ─ 連打！  {_grapple.InputCount}/{need}　{tail}";
```

`FindFirstObjectByType` は毎フレーム呼ばず、`_bellyPunchDirector` フィールドにキャッシュする。

**Step 2: テスト**

`UiRuntimeTests` が通ること。手動で `-grappletest` を起動し、表示を確認。

**Step 3: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/MatchHud.cs
git commit -m "feat: 後ろ拘束中のHUDに腹パンまでの残り時間を表示"
```

---

## Task 6: デバッグドライバとドキュメント

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Runtime/Debug/GrappleTestDriver.cs`
- Modify: `SPEC.md`
- Modify: `README.md`
- Modify: `Diagnostics.md`
- Modify: `Motion-Notes.md`
- Modify: `MioPrototype/Assets/RyonaCore/Runtime/CombatConstants.cs`

**Step 1: `-heldpunchshot=<png>`**

`GrappleTestDriver` で `HeldBellyPunchDirector.Performed` を購読し、
0.12 秒後に `ScreenCapture.CaptureScreenshot` する。

**Step 2: ドキュメント更新**

- SPEC §4.3: 後ろ拘束は時間切れなし、1.6 秒後に腹パン、被弾で解放
- README: 「後ろ拘束は時間切れで解けない。連打で逃げるか、腹パンを1回食らうか」
- Diagnostics: `-grappletest` の新しい挙動、`-heldpunchshot`
- Motion-Notes: 241 行目付近の「投げ」記述を修正
- CombatConstants: `GrappleLimitSeconds` は Pin 用と明記

**Step 3: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Runtime/Debug/GrappleTestDriver.cs \
        SPEC.md README.md Diagnostics.md Motion-Notes.md \
        MioPrototype/Assets/RyonaCore/Runtime/CombatConstants.cs
git commit -m "docs: 後ろ拘束の腹パン仕様と確認手順を更新"
```

---

## Task 7: 既存テストの更新と新規テスト

**Files:**
- Modify: `MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyAiRuntimeTests.cs`
- Modify: `MioPrototype/Assets/RyonaGame/Tests/PlayMode/PlayabilityRegressionTests.cs`
- Modify: `MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs`

**Step 1: 投げテストの更新**

`掴みの投げが終わった後に敵が攻撃を再開する` を
`後ろ拘束は時間切れで解けず振りほどくと敵が攻撃を再開する` に変更する。

- 拘束開始後 3.4 秒待ち、`grapple.IsGrappled` を確認
- private `Escape()` を reflection で呼ぶ
- その後、敵が攻撃を再開することを確認

**Step 2: `OtherEnemyCanDamagePlayerDuringBackGrab` の更新**

腹パンで拘束が解ける新仕様と衝突しないよう、攻撃側の `ForceZone = PunchZone.Face` にする。
または、演出ディレクターを無効化して「拘束中の被弾」だけを検証する。

**Step 3: `HeldBellyPunchTests` の完成**

- 後ろ拘束は時間切れでは解けない
- 腹パンで演出が始まり、カメラが寄る
- 0.55 秒後に拘束が解放される
- 顔パンチ／拘束者の攻撃では発動しない
- 連打で先に逃げると発動しない

**Step 4: 全テスト実行**

```
EditMode 19/19
PlayMode 44/44（既存38 + 新規6）
```

**Step 5: コミット**

```bash
git add MioPrototype/Assets/RyonaGame/Tests/PlayMode/EnemyAiRuntimeTests.cs \
        MioPrototype/Assets/RyonaGame/Tests/PlayMode/PlayabilityRegressionTests.cs \
        MioPrototype/Assets/RyonaGame/Tests/PlayMode/HeldBellyPunchTests.cs
git commit -m "test: 後ろ拘束の腹パン演出と解放の回帰テストを追加"
```

---

## Task 8: ビルドと実機確認

**Step 1: Windows ビルド**

```powershell
& "C:\Users\neoen\Unity\Editor\6000.4.5f1\Editor\Unity.exe" -batchmode `
  -projectPath "C:\gemini-desktop\ryona-action\MioPrototype" `
  -executeMethod Ryona.EditorTools.PlayerBuild.BuildWindows -quit
```

Expected: `[Ryona] build result=Succeeded size=109MB errors=0`

**Step 2: スモーク**

```powershell
.\MioPrototype\Build\MioPrototype.exe -grappletest -heldpunchshot=heldpunch.png
```

確認:
- 1.6 秒後に `[HeldPunch] trigger`
- 0.55 秒後に `[HeldPunch] release`
- 例外 0
- `heldpunch.png` で腹と顔が同時に写っている

**Step 3: PR**

```bash
git push -u origin feature/held-belly-punch
gh pr create --base main --title "feat: 後ろ拘束中の腹パン演出" --body "..."
```

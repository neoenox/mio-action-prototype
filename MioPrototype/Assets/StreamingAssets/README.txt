このフォルダに VRM を置くと、ゲームのカプセルが自動で差し替わります。

  ファイル名: mio_v1.vrm
  フルパス  : C:\gemini-desktop\ryona-action\MioPrototype\Assets\StreamingAssets\mio_v1.vrm

Unity を再生すれば読み込まれます（再インポート不要）。
ファイルが無いときはカプセルのまま動くので、ゲームは壊れません。

差し替え先: シーン内の "Mio" オブジェクトに付いた VrmCharacterSlot
  Placeholder = Visual（カプセル。VRM が来たら自動で非表示）
  Mount       = Mio 本体
  YawOffset   = 向き補正（正面が逆なら 180）

注意:
  - 当たり判定は Mio の CharacterController が持つので、VRM 側のコライダーは自動で無効化されます。
  - 身長は VRoid の設定がそのまま使われます。1.68m 前後を目安にしてください。

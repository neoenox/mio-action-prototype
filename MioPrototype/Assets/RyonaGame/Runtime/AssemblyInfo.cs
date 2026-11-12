using System.Runtime.CompilerServices;

// PlayMode テストは「プレイヤーが実際に触れる状態」を直接叩く。
// private のまま reflection で覗くと、リネームや分割のたびにテストが壊れるので、
// テスト対象の操作だけ internal にして、アセンブリ境界で守る。
[assembly: InternalsVisibleTo("Ryona.PlayTests")]
[assembly: InternalsVisibleTo("Ryona.Editor")]

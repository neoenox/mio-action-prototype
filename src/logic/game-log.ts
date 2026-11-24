import { LaunchOptions } from './launch-options';

/**
 * ゲーム内ログの唯一の出口。本番ビルドでは既定で黙る（Unity 版 GameLog 相当）。
 * 検証フラグ（log/hitlog/statlog/pinlog/jointlog/facelog）で有効になる。
 */
export class GameLog {
  private enabledValue: boolean;
  private readonly sink: (message: string) => void;

  constructor(options: LaunchOptions, development = true, sink: (message: string) => void = console.log) {
    this.sink = sink;
    this.enabledValue = development || ['log', 'hitlog', 'statlog', 'pinlog', 'jointlog', 'facelog']
      .some((flag) => options.has(flag));
  }

  get enabled(): boolean { return this.enabledValue; }

  setEnabled(value: boolean): void { this.enabledValue = value; }

  log(message: string): void {
    if (!this.enabledValue) return;
    this.sink(message);
  }
}

/** 検証フラグだけを拾う簡易ファクトリ。 */
export const verificationLog = (options: LaunchOptions, sink?: (m: string) => void): GameLog =>
  new GameLog(options, false, sink ?? console.log);

/**
 * 起動引数の読み取りを 1 か所にまとめる。Unity の LaunchOptions 相当。
 * ブラウザでは URLSearchParams、Node では string[] から作る。
 */
export class LaunchOptions {
  private readonly values = new Map<string, string>();
  private readonly switches = new Set<string>();
  readonly args: readonly string[];

  constructor(args: readonly string[] = []) {
    this.args = args;
    for (let i = 0; i < args.length; i++) {
      const a = args[i]!;
      if (a.length < 2 || a[0] !== '-') continue;
      const equals = a.indexOf('=');
      if (equals >= 0) {
        const name = a.slice(1, equals).toLowerCase();
        this.switches.add(name);
        this.values.set(name, a.slice(equals + 1));
        continue;
      }
      const flag = a.slice(1).toLowerCase();
      this.switches.add(flag);
      const next = args[i + 1];
      if (i + 1 < args.length && (next === undefined || next.length === 0 || next[0] !== '-')) {
        this.values.set(flag, next ?? '');
      }
    }
  }

  static fromSearch(search: string | URLSearchParams = ''): LaunchOptions {
    const params = search instanceof URLSearchParams ? search : new URLSearchParams(search);
    const args: string[] = [];
    // tsconfig の lib に DOM.Iterable が無いため for..of ではなく forEach を使う。
    params.forEach((value: string, key: string): void => {
      args.push(value === '' ? `-${key}` : `-${key}=${value}`);
    });
    return new LaunchOptions(args);
  }

  has(flag: string): boolean { return this.switches.has(this.key(flag)); }

  hasPrefix(prefix: string): boolean {
    const key = this.key(prefix);
    for (const name of this.switches) if (name.startsWith(key)) return true;
    return false;
  }

  value(key: string): string | null { return this.values.get(this.key(key)) ?? null; }

  tryFloat(key: string): number | null {
    const text = this.value(key);
    if (text === null) return null;
    const value = Number(text);
    return Number.isFinite(value) ? value : null;
  }

  tryInt(key: string): number | null {
    const value = this.tryFloat(key);
    return value === null ? null : Math.trunc(value);
  }

  private key(name: string): string {
    return name.length > 0 && name[0] === '-' ? name.slice(1).toLowerCase() : name.toLowerCase();
  }
}

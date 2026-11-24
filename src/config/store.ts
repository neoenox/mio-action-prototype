/** Unity の RyonaConfig 相当。起動引数クエリ > JSON > コード既定値の順で解決する。 */
export type ConfigSections = {
  camera: Record<string, number>;
  motor: Record<string, number>;
  combat: Record<string, number>;
  enemy: Record<string, number>;
  grapple: Record<string, number>;
  group: Record<string, number>;
  encounter: Record<string, number>;
  feel: Record<string, number>;
  cameraDirection: Record<string, number>;
};

const defaults: ConfigSections = {
  camera: {}, motor: {}, combat: {}, enemy: {}, grapple: {},
  group: {}, encounter: {}, feel: {}, cameraDirection: {},
};

export class ConfigStore {
  private sections: ConfigSections;
  private listeners: Array<(cfg: ConfigSections) => void> = [];

  constructor(query: URLSearchParams | Record<string, string> = new URLSearchParams()) {
    this.sections = structuredClone(defaults);
    const get = (k: string): string | null =>
      query instanceof URLSearchParams ? query.get(k) : (query[k] ?? null);
    for (const [key, value] of Object.entries(this.sections)) {
      for (const field of Object.keys(value)) {
        const override = get(`${key}.${field}`);
        if (override !== null && Number.isFinite(Number(override))) {
          value[field] = Number(override);
        }
      }
    }
  }

  get<K extends keyof ConfigSections>(section: K): Readonly<ConfigSections[K]> {
    return this.sections[section];
  }

  onChange(fn: (cfg: ConfigSections) => void): void {
    this.listeners.push(fn);
  }

  /** Unity の ConfigurableMonoBehaviour.OnConfigChanged 相当。 */
  notifyChanged(): void {
    for (const fn of this.listeners) fn(this.sections);
  }
}

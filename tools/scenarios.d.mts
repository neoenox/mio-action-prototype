/** tools/scenarios.mjs の型定義（Vitest / tsc から参照するため）。 */
export interface Scenario {
  name: string;
  args: string[];
  burst: string;
  frames: number;
  interval: number;
}

export declare const SCENARIOS: Record<string, Scenario>;
export declare const REEL_SCENARIOS: readonly ['bellybody', 'frontpin', 'haul'];
export declare const SCENARIO_NAMES: string[];
export declare function toQuery(spec: Scenario): string;
export declare function listScenarios(): string[];
declare const _default: Record<string, Scenario>;
export default _default;

import { describe, it, expect } from 'vitest';
import { SCENARIOS, REEL_SCENARIOS, SCENARIO_NAMES, toQuery } from '../../tools/scenarios.mjs';

describe('SCENARIOS（Unity 版 preview.py との対応）', () => {
  const expected = [
    'motionseq', 'wristlock', 'pinball', 'wristpull', 'wristpullcounter',
    'wallpin', 'frontpin', 'bearhug', 'wallsplit', 'belly', 'face',
    'facemount', 'bellybody', 'ankle', 'haul', 'haulprone', 'mount',
    'mountprone', 'idle',
  ];

  it('19 場面が定義順どおりに揃っている', () => {
    expect(SCENARIO_NAMES).toEqual(expected);
    expect(Object.keys(SCENARIOS)).toEqual(expected);
  });

  it('間隔は idle のみ 0.5 秒、他はすべて 0.05 秒', () => {
    for (const name of expected) {
      expect(SCENARIOS[name]!.interval, name).toBe(name === 'idle' ? 0.5 : 0.05);
    }
  });

  it('burst・frames が Unity 版と一致する', () => {
    const table: Record<string, [string, number]> = {
      motionseq: ['-motionseqburst', 760],
      wristlock: ['-wristlockburst', 140],
      pinball: ['-pinballburst', 320],
      wristpull: ['-wristlockburst', 220],
      wristpullcounter: ['-wristlockburst', 100],
      wallpin: ['-pinburst', 200],
      frontpin: ['-pinburst', 140],
      bearhug: ['-motionseqburst', 140],
      wallsplit: ['-pinburst', 360],
      belly: ['-heldpunchburst', 195],
      face: ['-heldpunchburst', 195],
      facemount: ['-heldpunchburst', 400],
      bellybody: ['-heldpunchburst', 195],
      ankle: ['-pinburst', 90],
      haul: ['-mountburst', 100],
      haulprone: ['-mountburst', 100],
      mount: ['-mountburst', 110],
      mountprone: ['-mountburst', 110],
      idle: ['-idleburst', 240],
    };
    for (const [name, [burst, frames]] of Object.entries(table)) {
      expect([SCENARIOS[name]!.burst, SCENARIOS[name]!.frames], name).toEqual([burst, frames]);
    }
  });

  it('--reel は bellybody / frontpin / haul の 3 本', () => {
    expect([...REEL_SCENARIOS]).toEqual(['bellybody', 'frontpin', 'haul']);
  });

  it('toQuery が Unity 引数を URL クエリへ変換する', () => {
    const q = new URLSearchParams(toQuery(SCENARIOS.frontpin!));
    expect(q.get('scenario')).toBe('frontpin');
    expect(q.get('grappletest')).toBe('1');
    expect(q.get('pintest')).toBe('1');
    expect(q.get('nowallpin')).toBe('1');

    const q2 = new URLSearchParams(toQuery(SCENARIOS.motionseq!));
    expect(q2.get('motionseq')).toBe('1,2,3,4,5,6,7,8,9,10');
    expect(q2.get('motionseqinterval')).toBe('3.5');
  });
});

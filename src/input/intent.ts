/** InputReader が logic へ渡す intent。Unity の InputReader 相当の境界。 */
export type InputIntent = {
  moveX: number;
  light: boolean;
  heavy: boolean;
  break_: boolean;
  struggle: boolean;
  restart: boolean;
  god: boolean;
};

export const emptyIntent = (): InputIntent => ({
  moveX: 0, light: false, heavy: false, break_: false,
  struggle: false, restart: false, god: false,
});

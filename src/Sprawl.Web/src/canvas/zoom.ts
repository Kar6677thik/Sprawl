/**
 * Semantic zoom. Three levels with hysteresis, so hovering at a boundary does not flicker between them:
 *   far  (< 0.35):  region titles and card counts; cards are quiet bars
 *   mid  (0.35–0.8): card titles and kind glyphs; connections without labels
 *   near (≥ 0.8):   card bodies and connection labels
 */
export type ZoomLevel = "far" | "mid" | "near";

export const FAR_BELOW = 0.35;
export const NEAR_FROM = 0.8;
export const HYSTERESIS = 0.04;

export function levelFor(zoom: number, previous?: ZoomLevel): ZoomLevel {
  const plain: ZoomLevel = zoom < FAR_BELOW ? "far" : zoom < NEAR_FROM ? "mid" : "near";
  if (!previous || previous === plain) return plain;
  // Leaving a level needs the zoom to pass the boundary by the hysteresis margin.
  if (previous === "far" && plain !== "far" && zoom < FAR_BELOW + HYSTERESIS) return "far";
  if (previous === "near" && plain !== "near" && zoom > NEAR_FROM - HYSTERESIS) return "near";
  if (previous === "mid") {
    if (plain === "far" && zoom > FAR_BELOW - HYSTERESIS) return "mid";
    if (plain === "near" && zoom < NEAR_FROM + HYSTERESIS) return "mid";
  }
  return plain;
}

/** The zoom that shows each level when jumping with the 1 / 2 / 3 keys. */
export const LEVEL_ZOOM: Record<ZoomLevel, number> = { far: 0.22, mid: 0.55, near: 1 };

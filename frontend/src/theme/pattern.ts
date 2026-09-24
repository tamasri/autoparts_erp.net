/**
 * The identity's geometric pattern (an eight-pointed star lattice, drawn in lines) as a CSS background image.
 * One 80×80 tile: a star at the centre and a quarter star at each corner, joined along the axes and the diagonals, so the
 * tiles run on without seams. Colour and strength are parameters; the pattern is always an outline, never filled.
 */

/** An eight-pointed star: two squares, one turned 45°, whose corners lie `r` from (cx, cy). */
const star = (cx: number, cy: number, r: number): string => {
  const h = r / Math.SQRT2;
  const square = `M${cx - h} ${cy - h}H${cx + h}V${cy + h}H${cx - h}Z`;
  const diamond = `M${cx} ${cy - r}L${cx + r} ${cy}L${cx} ${cy + r}L${cx - r} ${cy}Z`;
  return square + diamond;
};

const R = 16;
const D = R / Math.SQRT2;

const TILE_PATH = [
  star(40, 40, R), star(0, 0, R), star(80, 0, R), star(0, 80, R), star(80, 80, R),
  // centre star → tile edges (continues into the next tile) and → the corner stars
  `M40 ${40 - R}V0M40 ${40 + R}V80M${40 - R} 40H0M${40 + R} 40H80`,
  `M${40 - D} ${40 - D}L${D} ${D}M${40 + D} ${40 - D}L${80 - D} ${D}M${40 - D} ${40 + D}L${D} ${80 - D}M${40 + D} ${40 + D}L${80 - D} ${80 - D}`,
].join('');

/**
 * `background-image` value of the pattern.
 * @param color   line colour (normally Antique Sand or Golden Wheat)
 * @param opacity line opacity, 0–1 — keep it low behind content
 * @param width   line width in tile units
 */
export function patternImage(color: string, opacity: number, width = 1.2): string {
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='80' height='80' viewBox='0 0 80 80'>`
    + `<path d='${TILE_PATH}' fill='none' stroke='${color}' stroke-opacity='${opacity}' stroke-width='${width}' stroke-linejoin='round'/></svg>`;
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
}

/** Ready-made `sx` for a surface covered with the pattern. `size` scales the tile (80 px = natural size). */
export const patternSx = (color: string, opacity: number, size = 80, width = 1.2) => ({
  backgroundImage: patternImage(color, opacity, width),
  backgroundSize: `${size}px ${size}px`,
});

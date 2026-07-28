/** Minimal QR encoder (byte mode, versions 1–6, mask 0). No external deps. */

export type QrErrorCorrection = "L" | "M" | "Q" | "H";

const ECC_INDEX: Record<QrErrorCorrection, number> = { L: 0, M: 1, Q: 2, H: 3 };
const CAPACITY: Record<QrErrorCorrection, number[]> = {
  L: [17, 32, 53, 78, 106, 134],
  M: [14, 26, 42, 62, 84, 106],
  Q: [11, 20, 32, 46, 60, 74],
  H: [8, 14, 24, 34, 44, 58]
};
const VERSION_SIZE = [21, 25, 29, 33, 37, 41];
const EC_CW: number[][] = [
  [7, 10, 13, 17],
  [10, 16, 22, 28],
  [15, 26, 36, 44],
  [20, 36, 52, 64],
  [26, 48, 72, 88],
  [36, 64, 96, 112]
];
const BLOCKS: number[][] = [
  [1, 1, 1, 1],
  [1, 1, 1, 1],
  [1, 1, 2, 2],
  [1, 2, 2, 4],
  [1, 2, 4, 4],
  [2, 4, 4, 6]
];
const ALIGN_POS = [
  [],
  [6, 18],
  [6, 22],
  [6, 26],
  [6, 30],
  [6, 34]
];
const FORMAT_MASK0 = [0x77c4, 0x72f3, 0x7daa, 0x789d];

const EXP = new Uint8Array(512);
const LOG = new Uint8Array(256);
for (let i = 0, x = 1; i < 255; i++, x <<= 1) {
  if (x & 0x100) x ^= 0x11d;
  EXP[i] = x;
  LOG[x] = i;
}
for (let i = 255; i < 512; i++) EXP[i] = EXP[i - 255];

function gfMul(a: number, b: number): number {
  return !a || !b ? 0 : EXP[LOG[a] + LOG[b]];
}

function rsEncode(data: number[], ecCount: number): number[] {
  const gen = [1];
  for (let i = 0; i < ecCount; i++) {
    const next = new Array(gen.length + 1).fill(0);
    for (let j = 0; j < gen.length; j++) {
      next[j] ^= gen[j];
      next[j + 1] ^= gfMul(gen[j], EXP[i]);
    }
    gen.length = 0;
    gen.push(...next);
  }
  const ecc = new Array(ecCount).fill(0);
  for (const byte of data) {
    const factor = byte ^ ecc.shift();
    ecc.push(0);
    for (let i = 0; i < ecc.length; i++) ecc[i] ^= gfMul(gen[i], factor);
  }
  return ecc;
}

function pickVersion(byteLen: number, ec: QrErrorCorrection): number {
  for (let i = 0; i < CAPACITY[ec].length; i++) {
    if (byteLen <= CAPACITY[ec][i]) return i;
  }
  throw new Error("QR text too long");
}

function makeCodewords(bytes: Uint8Array, version: number, ec: QrErrorCorrection): number[] {
  const bits: number[] = [];
  const push = (val: number, len: number) => {
    for (let i = len - 1; i >= 0; i--) bits.push((val >> i) & 1);
  };
  push(4, 4);
  push(bytes.length, 8);
  for (const b of bytes) push(b, 8);
  const dataCw = CAPACITY.L[version];
  const totalCw = (VERSION_SIZE[version] ** 2) >> 3;
  const maxBits = totalCw * 8;
  while (bits.length < maxBits && bits.length % 8 !== 0) bits.push(0);
  const pad = [0xec, 0x11];
  let p = 0;
  while (bits.length < maxBits) {
    push(pad[p++ % 2], 8);
  }
  const data = bits.slice(0, dataCw * 8);
  const dataBytes: number[] = [];
  for (let i = 0; i < data.length; i += 8) {
    let byte = 0;
    for (let j = 0; j < 8; j++) byte = (byte << 1) | (data[i + j] || 0);
    dataBytes.push(byte);
  }

  const ecIdx = ECC_INDEX[ec];
  const ecCount = EC_CW[version][ecIdx];
  const blockCount = BLOCKS[version][ecIdx];
  const baseLen = Math.floor(dataBytes.length / blockCount);
  const blocks: number[][] = [];
  let offset = 0;
  for (let i = 0; i < blockCount; i++) {
    const chunk = dataBytes.slice(offset, offset + baseLen);
    offset += baseLen;
    blocks.push([...chunk, ...rsEncode(chunk, ecCount)]);
  }
  const out: number[] = [];
  const maxLen = Math.max(...blocks.map((b) => b.length));
  for (let i = 0; i < maxLen; i++) {
    for (const block of blocks) {
      if (block[i] != null) out.push(block[i]);
    }
  }
  return out.slice(0, totalCw);
}

function buildMatrix(version: number, codewords: number[], ec: QrErrorCorrection): boolean[][] {
  const size = VERSION_SIZE[version];
  const m = Array.from({ length: size }, () => Array<boolean>(size).fill(false));
  const reserved = Array.from({ length: size }, () => Array<boolean>(size).fill(false));

  const set = (r: number, c: number, dark: boolean, res = true) => {
    if (r < 0 || c < 0 || r >= size || c >= size) return;
    m[r][c] = dark;
    if (res) reserved[r][c] = true;
  };

  const finder = (r: number, c: number) => {
    for (let y = -1; y <= 7; y++) {
      for (let x = -1; x <= 7; x++) {
        const yy = r + y;
        const xx = c + x;
        if (yy < 0 || xx < 0 || yy >= size || xx >= size) continue;
        const on =
          (y >= 0 && y <= 6 && (x === 0 || x === 6)) ||
          (x >= 0 && x <= 6 && (y === 0 || y === 6)) ||
          (y >= 2 && y <= 4 && x >= 2 && x <= 4);
        set(yy, xx, on);
      }
    }
  };
  finder(0, 0);
  finder(0, size - 7);
  finder(size - 7, 0);

  const align = (r: number, c: number) => {
    for (let y = -2; y <= 2; y++) {
      for (let x = -2; x <= 2; x++) {
        const on = Math.max(Math.abs(x), Math.abs(y)) !== 1;
        set(r + y, c + x, on);
      }
    }
  };
  const positions = ALIGN_POS[version] || [];
  for (const ry of positions) {
    for (const cx of positions) {
      if ((ry < 7 && cx < 7) || (ry < 7 && cx > size - 8) || (ry > size - 8 && cx < 7)) continue;
      align(ry, cx);
    }
  }

  for (let i = 8; i < size - 8; i++) {
    set(6, i, i % 2 === 0);
    set(i, 6, i % 2 === 0);
  }
  set(size - 8, 8, true);

  const format = FORMAT_MASK0[ECC_INDEX[ec]];
  const placeFormat = (bits: number, startR: number, startC: number, dr: number, dc: number) => {
    for (let i = 0; i < 15; i++) {
      const dark = ((bits >> i) & 1) === 1;
      const r = startR + (dr < 0 ? -i * dr : i * dr);
      const c = startC + (dc < 0 ? -i * dc : i * dc);
      set(r, c, dark, true);
    }
  };
  placeFormat(format, 8, 0, 0, -1);
  placeFormat(format, size - 1, 8, -1, 0);
  placeFormat(format, 0, 8, 0, 1);
  placeFormat(format, 8, size - 1, 1, 0);

  let bitIdx = 0;
  let up = true;
  for (let col = size - 1; col > 0; col -= 2) {
    if (col === 6) col--;
    for (let i = 0; i < size; i++) {
      const r = up ? size - 1 - i : i;
      for (let dc = 0; dc < 2; dc++) {
        const c = col - dc;
        if (reserved[r][c]) continue;
        const byte = codewords[(bitIdx / 8) | 0] || 0;
        const bit = (byte >> (7 - (bitIdx % 8))) & 1;
        let dark = bit === 1;
        if (((r + c) & 1) === 0) dark = !dark;
        set(r, c, dark, true);
        bitIdx++;
      }
    }
    up = !up;
  }
  return m;
}

export function encodeQrMatrix(text: string, errorCorrection: QrErrorCorrection = "M"): boolean[][] {
  const bytes = new TextEncoder().encode(text);
  const version = pickVersion(bytes.length, errorCorrection);
  const codewords = makeCodewords(bytes, version, errorCorrection);
  return buildMatrix(version, codewords, errorCorrection);
}

export function qrMatrixToSvg(
  matrix: boolean[][],
  options?: { fg?: string; bg?: string; quietZone?: number; moduleSize?: number }
): string {
  const fg = options?.fg || "#000";
  const bg = options?.bg || "#fff";
  const quiet = Math.max(0, Number(options?.quietZone ?? 2));
  const module = Math.max(1, Number(options?.moduleSize ?? 1));
  const n = matrix.length;
  const dim = (n + quiet * 2) * module;
  let rects = "";
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      if (!matrix[r][c]) continue;
      const x = (c + quiet) * module;
      const y = (r + quiet) * module;
      rects += `<rect x="${x}" y="${y}" width="${module}" height="${module}"/>`;
    }
  }
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${dim} ${dim}" shape-rendering="crispEdges">` +
    `<rect width="100%" height="100%" fill="${bg}"/>` +
    `<g fill="${fg}">${rects}</g></svg>`
  );
}

export function encodeQrSvg(
  text: string,
  options?: Parameters<typeof qrMatrixToSvg>[1] & { errorCorrection?: QrErrorCorrection }
): string {
  return qrMatrixToSvg(encodeQrMatrix(text, options?.errorCorrection || "M"), options);
}

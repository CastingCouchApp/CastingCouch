/** Minimaler Text-Frame WebSocket (RFC6455) – nur Node-Builtins. */
import { createHash, randomBytes } from "node:crypto";

const OP_TEXT = 0x1;
const OP_CLOSE = 0x8;
const OP_PING = 0x9;
const OP_PONG = 0xa;

export function acceptKey(secWebSocketKey) {
  return createHash("sha1")
    .update(secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")
    .digest("base64");
}

export function encodeTextFrame(text) {
  const payload = Buffer.from(String(text), "utf8");
  const len = payload.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(2);
    header[0] = 0x80 | OP_TEXT;
    header[1] = len;
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[0] = 0x80 | OP_TEXT;
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x80 | OP_TEXT;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  return Buffer.concat([header, payload]);
}

function decodeFrames(buffer, onFrame) {
  let offset = 0;
  while (offset + 2 <= buffer.length) {
    const b0 = buffer[offset];
    const b1 = buffer[offset + 1];
    const opcode = b0 & 0x0f;
    const masked = (b1 & 0x80) !== 0;
    let payloadLen = b1 & 0x7f;
    let headerLen = 2;
    if (payloadLen === 126) {
      if (offset + 4 > buffer.length) break;
      payloadLen = buffer.readUInt16BE(offset + 2);
      headerLen = 4;
    } else if (payloadLen === 127) {
      if (offset + 10 > buffer.length) break;
      const big = buffer.readBigUInt64BE(offset + 2);
      payloadLen = Number(big);
      headerLen = 10;
    }
    const maskLen = masked ? 4 : 0;
    const total = headerLen + maskLen + payloadLen;
    if (offset + total > buffer.length) break;

    let payload = buffer.subarray(offset + headerLen + maskLen, offset + total);
    if (masked) {
      const mask = buffer.subarray(offset + headerLen, offset + headerLen + 4);
      const unmasked = Buffer.alloc(payloadLen);
      for (let i = 0; i < payloadLen; i++) {
        unmasked[i] = payload[i] ^ mask[i % 4];
      }
      payload = unmasked;
    }

    onFrame(opcode, payload);
    offset += total;
  }
  return buffer.subarray(offset);
}

/**
 * @param {import('node:http').IncomingMessage} req
 * @param {import('node:stream').Duplex} socket
 * @param {Buffer} head
 * @param {{ onMessage?: (text: string) => void, onClose?: () => void }} handlers
 */
export function upgradeWebSocket(req, socket, head, handlers = {}) {
  const key = req.headers["sec-websocket-key"];
  if (!key || typeof key !== "string") {
    socket.write("HTTP/1.1 400 Bad Request\r\n\r\n");
    socket.destroy();
    return null;
  }

  const headers = [
    "HTTP/1.1 101 Switching Protocols",
    "Upgrade: websocket",
    "Connection: Upgrade",
    `Sec-WebSocket-Accept: ${acceptKey(key)}`,
    "\r\n"
  ].join("\r\n");

  socket.write(headers);
  if (head && head.length) socket.unshift(head);

  let buf = Buffer.alloc(0);
  let closed = false;

  const client = {
    send(text) {
      if (closed) return;
      try {
        socket.write(encodeTextFrame(text));
      } catch {
        /* ignore */
      }
    },
    close() {
      if (closed) return;
      closed = true;
      try {
        const frame = Buffer.alloc(2);
        frame[0] = 0x80 | OP_CLOSE;
        frame[1] = 0;
        socket.write(frame);
      } catch {
        /* ignore */
      }
      socket.destroy();
      handlers.onClose?.();
    }
  };

  socket.on("data", (chunk) => {
    buf = Buffer.concat([buf, chunk]);
    buf = decodeFrames(buf, (opcode, payload) => {
      if (opcode === OP_CLOSE) {
        client.close();
        return;
      }
      if (opcode === OP_PING) {
        const frame = Buffer.alloc(2 + payload.length);
        frame[0] = 0x80 | OP_PONG;
        frame[1] = payload.length;
        payload.copy(frame, 2);
        socket.write(frame);
        return;
      }
      if (opcode === OP_TEXT) {
        handlers.onMessage?.(payload.toString("utf8"));
      }
    });
  });

  socket.on("close", () => {
    if (!closed) {
      closed = true;
      handlers.onClose?.();
    }
  });
  socket.on("error", () => client.close());

  return client;
}

export function randomId() {
  return randomBytes(4).toString("hex");
}

// Observe only the verified RC003 synchronous, nine-byte HidOverGatt format.
// No report modification, input injection, sockets, files, or permanent scripts.
'use strict';
if (Process.platform !== 'windows' || Process.arch !== 'x64' || Process.pointerSize !== 8)
  throw new Error('unsupported_host');
const targetNames = new Map([[0xf1, 'back'], [0x80, 'volume_up'], [0x81, 'volume_down']]);
const known = new Set([0xf1, 0x80, 0x81, 0x65, 0x4a, 0x35, 0x52, 0x51,
  0x50, 0x4f, 0x28, 0x66, 0x3e]);
const streams = new Map();
const counts = { pending: 0, unknown: 0, invalid: 0 };
let listener = null;
let closeListener = null;
let nextStream = 1;
let timer = null;
let stopped = false;
let leaseUntil = Date.now() + 10000;
function stop(reason) {
  if (stopped) return;
  stopped = true;
  if (timer !== null) clearInterval(timer);
  if (listener !== null) listener.detach();
  if (closeListener !== null) closeListener.detach();
  streams.clear();
  send({ kind: 'stopped', reason });
}
listener = Interceptor.attach(Process.getModuleByName('ntdll.dll')
  .getExportByName('NtDeviceIoControlFile'), {
  onEnter(args) {
    this.capture = !stopped && args[5].toUInt32() === 0x80018483
      && args[9].toUInt32() === 9;
    if (!this.capture) return;
    this.output = args[8];
    this.iosb = args[4];
    this.file = args[0].toString();
  },
  onLeave(result) {
    if (!this.capture || stopped) return;
    const status = result.toUInt32();
    if (status === 0x103) { counts.pending++; return; }
    if (status !== 0) return;
    try {
      if (this.output.isNull() || this.iosb.isNull() || this.iosb.readU32() !== 0) return;
      // Information=0 was observed on the user's RC003 despite a valid buffer.
      const length = this.iosb.add(8).readU64().toNumber();
      if (length !== 0 && length !== 9) { counts.invalid++; return; }
      const bytes = new Uint8Array(this.output.readByteArray(9));
      if (bytes[0] !== 1 || bytes[1] !== 0 || bytes[2] !== 0) return;
      let stream = streams.get(this.file);
      if (!stream) {
        if (nextStream > 16) { stop('stream_limit'); return; }
        stream = { id: nextStream++, signature: null };
        streams.set(this.file, stream);
      }
      const active = new Set();
      let other = false;
      for (let offset = 3; offset < 9; offset += 2) {
        const usage = bytes[offset] | (bytes[offset + 1] << 8);
        if (usage === 0) continue;
        if (!known.has(usage)) {
          counts.unknown++;
          if (stream.signature !== 'invalid') {
            stream.signature = 'invalid';
            send({ kind: 'invalid', stream: stream.id });
          }
          return;
        }
        const name = targetNames.get(usage);
        if (name === undefined) other = true;
        else active.add(name);
      }
      const names = Array.from(active).sort();
      const signature = names.join(',') + ':' + other;
      if (stream.signature === signature) return;
      stream.signature = signature;
      send({ kind: 'report', stream: stream.id, active: names, other });
    } catch (_) {
      counts.invalid++;
      const stream = streams.get(this.file);
      if (stream && stream.signature !== 'invalid') {
        stream.signature = 'invalid';
        send({ kind: 'invalid', stream: stream.id });
      }
    }
  }
});
// Device reconnection may reuse a numeric handle inside the same WUDFHost.
// Revoke its calibrated identity before any later report can inherit it.
closeListener = Interceptor.attach(Process.getModuleByName('ntdll.dll')
  .getExportByName('NtClose'), {
  onEnter(args) {
    if (stopped) return;
    const file = args[0].toString();
    const stream = streams.get(file);
    if (stream === undefined) return;
    // Invalidate on entry so another thread cannot reuse the handle before
    // onLeave. A failed close causes a harmless request to calibrate again.
    streams.delete(file);
    send({ kind: 'invalid', stream: stream.id });
  }
});
timer = setInterval(() => {
  if (Date.now() >= leaseUntil) stop('lease_expired');
}, 1000);
rpc.exports = {
  renewLease() {
    if (stopped) return false;
    leaseUntil = Date.now() + 10000;
    return true;
  },
  stop() { stop('requested'); }
};
send({ kind: 'ready' });

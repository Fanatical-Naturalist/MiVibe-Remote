// Bounded observation of the selected RC003 driver host. No input injection,
// suppression, buffer writes, persistent hooks, or logging of unknown usages.
'use strict';
const durationMs = __DURATION_MS__;
if (Process.platform !== 'windows' || Process.arch !== 'x64') {
  throw new Error('This probe requires a Windows x64 driver host.');
}
const names = new Map([
  [0xf1, 'back'], [0x80, 'volume_up'], [0x81, 'volume_down'],
  [0x65, 'menu'], [0x4a, 'home'], [0x35, 'tv'],
  [0x52, 'up'], [0x51, 'down'], [0x50, 'left'], [0x4f, 'right'],
  [0x28, 'ok'], [0x66, 'power'], [0x3e, 'microphone']
]);
const streams = new Map();
const stats = { totalCalls: 0, ioctlCandidates: 0, matchingCalls: 0, pending: 0, failed: 0, validReports: 0,
  badHeader: 0, badLength: 0, unknownUsages: 0, readErrors: 0, unchanged: 0 };
const capacities = {};
const actualLengths = {};
const ioctls = {};
let listener = null;
let stopped = false;
let watchdog = null;
let heartbeat = null;
function summary() { return { stats, capacities, actualLengths, ioctls }; }
function stop() {
  if (stopped) return stats;
  stopped = true;
  if (watchdog !== null) clearTimeout(watchdog);
  if (heartbeat !== null) clearInterval(heartbeat);
  if (listener !== null) listener.detach();
  streams.clear();
  send({ kind: 'stopped', ...summary() });
  return stats;
}
listener = Interceptor.attach(Process.getModuleByName('ntdll.dll')
  .getExportByName('NtDeviceIoControlFile'), {
  onEnter(args) {
    this.matches = false;
    if (stopped) return;
    stats.totalCalls++;
    const ioctl = args[5].toUInt32();
    const ioctlKey = '0x' + ioctl.toString(16);
    if (ioctls[ioctlKey] !== undefined || Object.keys(ioctls).length < 32)
      ioctls[ioctlKey] = (ioctls[ioctlKey] || 0) + 1;
    if (ioctl !== 0x80018483) return;
    stats.ioctlCandidates++;
    const capacity = args[9].toUInt32();
    if (capacities[capacity] !== undefined || Object.keys(capacities).length < 32)
      capacities[capacity] = (capacities[capacity] || 0) + 1;
    this.matches = capacity === 9;
    if (!this.matches) return;
    stats.matchingCalls++;
    this.output = args[8];
    this.iosb = args[4];
    this.file = args[0].toString();
  },
  onLeave(result) {
    if (!this.matches || stopped) return;
    const status = result.toUInt32();
    // Do not dereference a pending buffer after returning to its owner.
    // Pending calls are counted; this first probe does not claim async coverage.
    if (status === 0x103) { stats.pending++; return; }
    if (status !== 0) { stats.failed++; return; }
    try {
      if (this.iosb.isNull() || this.output.isNull()) { stats.readErrors++; return; }
      const finalStatus = this.iosb.readU32();
      const actualBytes = this.iosb.add(Process.pointerSize).readU64().toNumber();
      if (actualLengths[actualBytes] !== undefined || Object.keys(actualLengths).length < 32)
        actualLengths[actualBytes] = (actualLengths[actualBytes] || 0) + 1;
      if (finalStatus !== 0) { stats.failed++; return; }
      // This private RC003 path may not report nine bytes in Information.
      // Observe the explicitly supplied nine-byte buffer after synchronous
      // success, validating the full header and every usage before logging.
      // Retain Information as diagnostic metadata, not a universal contract.
      if (actualBytes !== 9) stats.badLength++;
      const bytes = new Uint8Array(this.output.readByteArray(9));
      if (bytes[0] !== 1 || bytes[1] !== 0 || bytes[2] !== 0) {
        stats.badHeader++; return;
      }
      const active = new Set();
      for (let offset = 3; offset < 9; offset += 2) {
        const usage = bytes[offset] | (bytes[offset + 1] << 8);
        if (usage === 0) continue;
        if (!names.has(usage)) { stats.unknownUsages++; return; }
        active.add(usage);
      }
      stats.validReports++;
      let stream = streams.get(this.file);
      if (!stream) {
        if (streams.size >= 16) return;
        stream = { id: streams.size + 1, active: new Set() };
        streams.set(this.file, stream);
      }
      const down = [...active].filter(usage => !stream.active.has(usage));
      const up = [...stream.active].filter(usage => !active.has(usage));
      stream.active = active;
      if (down.length === 0 && up.length === 0) { stats.unchanged++; return; }
      send({ kind: 'keys', stream: stream.id, information: actualBytes,
        down: down.map(usage => names.get(usage)), up: up.map(usage => names.get(usage)),
        active: [...active].map(usage => names.get(usage)) });
    } catch (_) { stats.readErrors++; }
  }
});
watchdog = setTimeout(stop, durationMs);
heartbeat = setInterval(() => send({ kind: 'progress', ...summary() }), 15000);
rpc.exports = { stop, stats: summary };
send({ kind: 'ready', durationSeconds: durationMs / 1000,
  observationOnly: true, asyncCoverage: false });

// Execute the parser against mock Frida pointers only. No target attachment.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'hid_tap.js'), 'utf8');

function harness() {
  const output = [];
  const hooks = new Map();
  const detached = [];
  const intervals = [];
  let now = 0;
  const context = {
    Process: {platform: 'windows', arch: 'x64', pointerSize: 8,
      getModuleByName: () => ({getExportByName: name => name})},
    Interceptor: {attach: (name, callbacks) => {
      hooks.set(name, callbacks);
      return {detach: () => detached.push(name)};
    }},
    send: message => output.push(JSON.parse(JSON.stringify(message))),
    Date: {now: () => now},
    setInterval: callback => {intervals.push(callback); return intervals.length;},
    clearInterval: () => {}, rpc: {exports: {}}, Uint8Array, Map, Set, Error,
  };
  vm.runInNewContext(source, context, {timeout: 1000});
  const pointer = value => ({toUInt32: () => value >>> 0, toString: () => String(value)});
  function emit(usages, {file = 42, information = 0, status = 0, capacity = 9,
                         header = [1, 0, 0]} = {}) {
    const bytes = Uint8Array.from([...header, 0, 0, 0, 0, 0, 0]);
    usages.forEach((value, index) => {bytes[3 + index * 2] = value & 255;
      bytes[4 + index * 2] = value >> 8;});
    const args = Array(10).fill(pointer(0));
    args[0] = pointer(file);
    args[4] = {isNull: () => false, readU32: () => 0,
      add: () => ({readU64: () => ({toNumber: () => information})})};
    args[5] = pointer(0x80018483);
    args[8] = {isNull: () => false, readByteArray: () => bytes.buffer};
    args[9] = pointer(capacity);
    const state = {};
    const hook = hooks.get('NtDeviceIoControlFile');
    hook.onEnter.call(state, args);
    hook.onLeave.call(state, pointer(status));
  }
  return {output, emit, hooks, detached, context,
    close: file => hooks.get('NtClose').onEnter.call({}, [pointer(file)]),
    advance: milliseconds => {now += milliseconds; intervals.forEach(callback => callback());}};
}

{
  const h = harness();
  assert.deepEqual(h.output.shift(), {kind: 'ready'});
  h.emit([0xf1]);
  assert.deepEqual(h.output.pop(), {kind: 'report', stream: 1, active: ['back'], other: false});
  h.emit([0xf1]);
  assert.equal(h.output.length, 0, 'held repeats are suppressed');
  h.emit([]);
  assert.deepEqual(h.output.pop().active, []);
  h.emit([0x80], {information: 9});
  assert.deepEqual(h.output.pop().active, ['volume_up']);
  h.emit([0x81]);
  assert.deepEqual(h.output.pop().active, ['volume_down']);
  h.emit([0x35]);
  assert.deepEqual(h.output.pop(), {kind: 'report', stream: 1, active: [], other: true});
  h.emit([0xabcd]);
  assert.deepEqual(h.output.pop(), {kind: 'invalid', stream: 1});
  h.emit([0xabcd]);
  assert.equal(h.output.length, 0, 'unknown values never leave the script');
}
{
  const h = harness();
  h.output.length = 0;
  for (const options of [{status: 0x103}, {status: 0xc0000001}, {capacity: 10},
                         {information: 8}, {header: [0, 0, 0]}]) h.emit([0xf1], options);
  assert.equal(h.output.length, 0, 'unsupported reports are ignored');
  h.emit([0xf1]);
  h.close(42);
  assert.deepEqual(h.output.pop(), {kind: 'invalid', stream: 1});
  h.emit([0x80]);
  assert.equal(h.output.pop().stream, 2, 'reused handle must have new stream identity');
}
{
  const h = harness();
  h.advance(9000);
  assert.equal(h.detached.length, 0);
  assert.equal(h.context.rpc.exports.renewLease(), true);
  h.advance(9000);
  assert.equal(h.detached.length, 0);
  h.advance(1000);
  assert.deepEqual(h.detached.sort(), ['NtClose', 'NtDeviceIoControlFile']);
  assert.deepEqual(h.output.pop(), {kind: 'stopped', reason: 'lease_expired'});
  assert.equal(h.context.rpc.exports.renewLease(), false);
  h.output.length = 0;
  h.emit([0xf1]);
  assert.equal(h.output.length, 0);
}
{
  const h = harness();
  for (let file = 1; file <= 17; file++) h.emit([0xf1], {file});
  assert.deepEqual(h.output.pop(), {kind: 'stopped', reason: 'stream_limit'});
  assert.equal(h.detached.length, 2, 'stream identity storage is bounded');
}
process.stdout.write('HID parser, stream identity and lease mock checks passed.\n');

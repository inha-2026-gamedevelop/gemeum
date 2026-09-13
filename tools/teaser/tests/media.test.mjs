import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validatePlan, audioGain} from '../src/plan.mjs';
import {verifyVideo, verifyAudio} from '../scripts/media.mjs';

const source = JSON.parse(readFileSync(new URL('../../../Assets/09.Data/Teaser/teaser-shots.json', import.meta.url), 'utf8'));
const video = () => ({streams: [{codec_type: 'video', width: 1920, height: 1080, avg_frame_rate: '60/1', r_frame_rate: '60/1', nb_read_frames: '300'}]});
test('rejects file metadata that differs from declared take and trim', () => {
  const shot = {...source.shots[0], sourceInFrame: 60}, take = {id: 'one', fps: 60, frameCount: 300};
  assert.doesNotThrow(() => verifyVideo(video(), source, shot, take));
  assert.throws(() => verifyVideo(video(), source, {...shot, sourceInFrame: 61}, take), /too short/);
  assert.throws(() => verifyVideo(video(), source, shot, {...take, frameCount: 301}), /differs/);
  for (const [key, value] of [['width', 1280], ['avg_frame_rate', '60000/1001'], ['r_frame_rate', '120/1'], ['nb_read_frames', 'N/A']]) {
    const probe = video(); probe.streams[0][key] = value;
    assert.throws(() => verifyVideo(probe, source, shot, take));
  }
});
test('audio must contain the complete source range', () => {
  const cue = {id: 'voice', sourceInFrame: 60, durationFrames: 120};
  assert.doesNotThrow(() => verifyAudio({streams: [{codec_type: 'audio', duration: '3'}]}, source, cue));
  assert.throws(() => verifyAudio({streams: [{codec_type: 'audio', duration: '2.999'}]}, source, cue), /too short/);
  assert.throws(() => verifyAudio(video(), source, cue), /stream missing/);
});
test('audio envelope is relative to the cue, not the source trim or global time', () => {
  const cue = {sourceInFrame: 600, startFrame: 2880, durationFrames: 120, volume: .5, fadeInFrames: 12, fadeOutFrames: 12};
  assert.equal(audioGain(cue, -1), 0);
  assert.equal(audioGain(cue, 0), 0);
  assert.equal(audioGain(cue, 6), .25);
  assert.equal(audioGain(cue, 60), .5);
  assert.equal(audioGain(cue, 113), .25);
  assert.equal(audioGain(cue, 119), 0);
  assert.equal(audioGain(cue, 120), 0);
});
test('invalid audio mix values fail before rendering', () => {
  for (const edit of [{sourceInFrame: -.5}, {volume: 2}, {fadeInFrames: 3000}, {fadeOutFrames: 1.5}]) {
    const plan = structuredClone(source);
    Object.assign(plan.audioCues[0], edit);
    assert.ok(validatePlan(plan).length > 0);
  }
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {captionFile, inkAmount, subtitleOpacity} from '../src/editorial.mjs';
import {readLoudness} from '../scripts/master-audio.mjs';
const plan = JSON.parse(readFileSync(new URL('../../../Assets/09.Data/Teaser/teaser-shots.json', import.meta.url), 'utf8'));
test('ink hides both frames of a cut and returns fully transparent', () => {
  const overlay = plan.overlays.find(o => o.id === 'gat-match');
  const cut = plan.shots.find(s => s.id === overlay.targetShotId).startFrame;
  assert.equal(inkAmount(overlay, overlay.startFrame, plan), 0);
  assert.equal(inkAmount(overlay, cut-1, plan), 1);
  assert.equal(inkAmount(overlay, cut, plan), 1);
  assert.equal(inkAmount(overlay, overlay.startFrame+overlay.durationFrames-1, plan), 0);
  assert.equal(inkAmount(overlay, overlay.startFrame+overlay.durationFrames, plan), 0);
});
test('ending ink reaches opaque before four-second title hold', () => {
  const overlay = plan.overlays.find(o => o.id === 'title-ink');
  assert.equal(inkAmount(overlay, 4080, plan), 0);
  assert.equal(inkAmount(overlay, 4139, plan), 1);
});
test('subtitle fades only within its absolute cue and exports exact sidecar times', () => {
  const overlay = plan.overlays.find(o => o.kind === 'subtitle');
  assert.equal(subtitleOpacity(overlay,1199),0);
  assert.equal(subtitleOpacity(overlay,1210),1);
  assert.equal(subtitleOpacity(overlay,1439),0);
  assert.equal(subtitleOpacity(overlay,1440),0);
  assert.ok(captionFile(plan).includes('00:00:20,000 --> 00:00:24,000'));
  assert.ok(captionFile(plan,'vtt').startsWith('WEBVTT\n\n'));
});
test('silent or non-measurable audio is never declared mastered', () => {
  assert.throws(() => readLoudness('no audio'), /missing/);
  assert.throws(() => readLoudness('{"input_i":"-inf","input_tp":"-inf","input_lra":"0","input_thresh":"-70","target_offset":"inf"}'), /silent/);
});

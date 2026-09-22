import assert from 'node:assert/strict';
import {mkdirSync, readFileSync, writeFileSync, copyFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {bundle} from '@remotion/bundler';
import {selectComposition, renderStill, renderMedia} from '@remotion/renderer';
import {probeMedia, videoMetadata, verifyVideo, verifyAudio} from './media.mjs';
import {validatePlan} from '../src/plan.mjs';
import {registerMedia} from './register-media.mjs';

// Synthetic sources are isolated and never registered in the production manifest
const root = fileURLToPath(new URL('../', import.meta.url));
const id = `verify-${randomUUID()}`;
const mediaRoot = resolve(root, 'public/media', id);
const output = resolve(root, 'out', id);
mkdirSync(mediaRoot, {recursive: true}); mkdirSync(output, {recursive: true});
function ffmpeg(args, binary = false) {
  const result = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', ...args], {encoding: binary ? undefined : 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 16 * 1024 * 1024});
  if (result.error || result.status !== 0) throw new Error(`ffmpeg: ${result.error?.message ?? result.stderr}`);
  return result.stdout;
}
const video = resolve(mediaRoot, 'synthetic-colors.mp4'), sound = resolve(mediaRoot, 'synthetic-tone.wav');
ffmpeg(['-f', 'lavfi', '-i', 'color=red:s=1920x1080:r=60:d=0.5', '-f', 'lavfi', '-i', 'color=blue:s=1920x1080:r=60:d=5.5', '-filter_complex', '[0:v][1:v]concat=n=2:v=1:a=0[out]', '-map', '[out]', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '18', '-pix_fmt', 'yuv420p', video]);
ffmpeg(['-f', 'lavfi', '-i', "aevalsrc='if(lt(t,1),0,0.5*sin(2*PI*440*t))':s=48000:d=6", sound]);
const manifest = resolve(root, '../../Assets/09.Data/Teaser/teaser-shots.json');
const original = readFileSync(manifest, 'utf8');
const isolated = resolve(output, 'isolated-repository');
const isolatedManifest = resolve(isolated, 'Assets/09.Data/Teaser/teaser-shots.json');
mkdirSync(resolve(isolated, 'Assets/09.Data/Teaser'), {recursive: true});
mkdirSync(resolve(isolated, 'tools/teaser/public/media'), {recursive: true});
writeFileSync(isolatedManifest, original);
copyFileSync(video, resolve(isolated, 'tools/teaser/public/media/test.mp4'));
copyFileSync(sound, resolve(isolated, 'tools/teaser/public/media/test.wav'));
const shotOptions = {shot: 'A1-01-lantern', take: 'verified-test', file: 'media/test.mp4', 'source-in': '60'};
registerMedia(shotOptions, isolated);
let registered = JSON.parse(readFileSync(isolatedManifest, 'utf8'));
assert.equal(registered.shots[0].sourceInFrame, 60);
assert.equal(registered.shots[0].takes[0].frameCount, 360);
assert.equal(registered.shots[0].setupStatus, 'pending');
assert.deepEqual(registered.shots.slice(1), JSON.parse(original).shots.slice(1));
const registeredText = readFileSync(isolatedManifest, 'utf8');
assert.throws(() => registerMedia(shotOptions, isolated), /already exists/);
assert.throws(() => registerMedia({...shotOptions, take: 'short', 'source-in': '121'}, isolated), /too short/);
assert.throws(() => registerMedia({...shotOptions, file: 'media/../test.mp4'}, isolated), /Unsafe/);
assert.equal(readFileSync(isolatedManifest, 'utf8'), registeredText);
registerMedia({audio: 'voice-C3-03', file: 'media/test.wav', 'source-in': '60'}, isolated);
registered = JSON.parse(readFileSync(isolatedManifest, 'utf8'));
assert.equal(registered.audioCues.find(c => c.id === 'voice-C3-03').sourceInFrame, 60);
const plan = JSON.parse(original);
const metadata = videoMetadata(probeMedia(video), plan);
for (const [i, trim] of [[0,60], [1,0]]) {
  const shot = plan.shots[i]; shot.sourceInFrame = trim;
  shot.takes = [{id: 'synthetic-verification-only', path: `media/${id}/synthetic-colors.mp4`, ...metadata, origin: 'unity-recorder'}];
  shot.selectedTakeId = shot.takes[0].id;
  verifyVideo(probeMedia(video), plan, shot, shot.takes[0]);
}
// 'origin' here only exercises the contract. This test pattern is not Unity footage
plan.audioCues = [{id: 'test-tone', startFrame: 0, durationFrames: 240, sourceInFrame: 60, volume: 1, label: '검증용 합성음', assetPath: `media/${id}/synthetic-tone.wav`}];
verifyAudio(probeMedia(sound), plan, plan.audioCues[0]);
assert.deepEqual(validatePlan(plan), []);
const serveUrl = await bundle({entryPoint: resolve(root, 'src/index.jsx'), publicDir: resolve(root, 'public'), outDir: resolve(output, 'bundle')});
const inputProps = {plan};
const composition = await selectComposition({serveUrl, id: 'TeaserReview', inputProps});
for (const [frame, channel] of [[0,2], [239,2], [240,0], [269,0], [270,2]]) {
  const path = resolve(output, `frame-${frame}.png`);
  await renderStill({serveUrl, composition, inputProps, frame, output: path, imageFormat: 'png'});
  const rgb = ffmpeg(['-i', path, '-vf', 'crop=2:2:960:540,scale=1:1', '-frames:v', '1', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'], true);
  assert.ok(rgb[channel] > 200 && rgb[(channel+1)%3] < 30, `Wrong source frame at ${frame}: ${[...rgb]}`);
}
const clip = resolve(output, 'synthetic-review.mp4');
await renderMedia({serveUrl, composition, inputProps, codec: 'h264', audioCodec: 'aac', outputLocation: clip, frameRange: [0,59], concurrency: 2});
const samples = ffmpeg(['-i', clip, '-vn', '-ac', '1', '-ar', '48000', '-f', 'f32le', 'pipe:1'], true);
let sum = 0, count = 0;
// First 0.2s must already contain sound: source silence was trimmed off
for (let offset = 0; offset < Math.min(samples.length, 9600*4); offset += 4) { sum += samples.readFloatLE(offset) ** 2; count++; }
assert.ok(count > 0 && Math.sqrt(sum/count) > .2, 'Trimmed audio is silent or attenuated unexpectedly.');
assert.equal(readFileSync(manifest, 'utf8'), original, 'Production manifest changed during verification.');
writeFileSync(resolve(output, 'result.json'), JSON.stringify({passed: true, syntheticOnly: true, registrationAndFailurePreservation: true, frames: [0,239,240,269,270], audioFirst200msRms: Math.sqrt(sum/count), productionManifestUnchanged: true}, null, 2));
console.log(`PASS media integration: source trim, shot boundaries, audio trim. Evidence: ${output}`);

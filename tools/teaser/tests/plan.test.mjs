import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validatePlan, activeAt, readiness} from '../src/plan.mjs';

const source = JSON.parse(readFileSync(new URL('../../../Assets/09.Data/Teaser/teaser-shots.json', import.meta.url), 'utf8'));
const copy = () => structuredClone(source);
test('production draft covers every output frame once', () => {
  assert.deepEqual(validatePlan(source), []);
  for (let frame = 0; frame < source.totalFrames; frame++) assert.equal(activeAt(source.shots, frame).length, 1);
  assert.equal(activeAt(source.shots, source.totalFrames).length, 0);
});
for (const [name, mutate] of [
  ['gap', p => p.shots[1].startFrame++],
  ['overlap', p => p.shots[1].startFrame--],
  ['fractional frames', p => p.shots[0].durationFrames += .5],
  ['wrong duration', p => p.totalFrames++],
  ['gameplay slowdown', p => p.shots.find(s => s.kind === 'gameplay').timeScale = .25],
  ['duplicate id', p => p.shots[1].id = p.shots[0].id],
  ['missing scene', p => p.shots[0].scene = ''],
  ['unknown requirement', p => p.shots[0].requirements = ['unknown']],
  ['bad camera', p => p.shots[0].cameraFrom = {position: [0,0,0], euler: [0,0,0], fov: 180}],
  ['trigger outside shot', p => p.shots[0].triggers = [{kind:'show', clock:'output', offsetFrame:240}]],
  ['ambiguous trigger clock', p => p.shots[0].triggers = [{kind:'show', clock:'simulation', offsetFrame:0}]],
  ['unknown take', p => p.shots[0].selectedTakeId = 'missing'],
  ['invalid transition target', p => p.overlays[0].targetShotId = p.shots[5].id],
  ['audio out of range', p => p.audioCues[0].durationFrames = 5000],
  ['null shot', p => p.shots[0] = null],
]) test(`rejects ${name}`, () => { const p=copy(); mutate(p); assert.ok(validatePlan(p).length > 0); });
test('cannot treat unrecorded plan as ready', () => {
  const results = readiness(source);
  assert.equal(results.length, source.shots.length+source.audioCues.length);
  assert.equal(results.filter(s => s.missing.includes('녹화 원본 없음')).length, source.shots.length-1);
  assert.deepEqual(results.find(s => s.id === 'END-title').missing, []);
  assert.ok(results.find(s => s.id === 'audio:voice-C3-03').missing.includes('음원 원본 없음'));
});
test('title has 60 frames of transition then 240 frames of readable hold', () => {
  assert.equal(activeAt(source.overlays, 4139).some(o=>o.kind==='title'), false);
  assert.equal(activeAt(source.overlays, 4140).find(o=>o.kind==='title').text, '그믐 (晦)');
  assert.equal(activeAt(source.overlays, 4379).some(o=>o.kind==='title'), true);
});
test('media paths cannot escape local media directory; short takes are rejected', () => {
  const p=copy(), s=p.shots[0];
  s.takes=[{id:'take1',path:'media/../secret.mp4',fps:60,frameCount:240,origin:'unity-recorder'}];
  s.selectedTakeId='take1';
  assert.ok(validatePlan(p).some(e=>e.includes('Unsafe media')));
  s.takes[0].path='media/shot1.mp4'; s.sourceInFrame=30;
  assert.ok(validatePlan(p).some(e=>e.includes('Insufficient')));
  s.takes[0].frameCount=270;
  assert.deepEqual(validatePlan(p),[]);
});

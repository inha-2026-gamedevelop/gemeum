import {readFileSync, writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import assert from 'node:assert/strict';
import {probeMedia, videoMetadata} from './media.mjs';

const root = fileURLToPath(new URL('../',import.meta.url));
const output = resolve(root,'out/graphics');
const manifest = JSON.parse(readFileSync(resolve(output,'manifest.json'),'utf8'));
function pixels(file, frame, alpha = false) {
  const result = spawnSync('ffmpeg',['-hide_banner','-loglevel','error','-i',resolve(output,file),'-vf',`select=eq(n\\,${frame})${alpha ? ',alphaextract' : ''}`,'-frames:v','1','-f','rawvideo','-pix_fmt',alpha ? 'gray' : 'rgb24','pipe:1'],{windowsHide:true,maxBuffer:16*1024*1024});
  if (result.error || result.status !== 0) throw new Error(String(result.error?.message ?? result.stderr));
  return result.stdout;
}
for (const spec of manifest.files) {
  const metadata = videoMetadata(probeMedia(resolve(output,spec.file)), manifest);
  assert.equal(metadata.frameCount,spec.frames);
}
const blank = pixels('ink-transition-alpha.mov',0,true);
assert.equal(blank.length,1920*1080);
assert.ok(blank.every(v=>v===0),'First transition frame must be transparent.');
for (const frame of [89,90]) assert.ok(pixels('ink-transition-alpha.mov',frame,true).every(v=>v===255),'Both sides of the cut must be fully opaque.');
assert.ok(pixels('ink-transition-alpha.mov',119,true).every(v=>v===0),'Last transition frame must be transparent.');
const middle = pixels('ink-transition-alpha.mov',40,true);
assert.ok(middle.some(v=>v===0) && middle.some(v=>v===255),'Intermediate ink must have covered and transparent areas.');
const subtitle = pixels('subtitle-alpha.mov',100,true);
assert.ok(subtitle.subarray(0,1920*700).every(v=>v===0),'Subtitle must preserve the upper image.');
assert.ok(subtitle.some(v=>v>200),'Subtitle alpha is empty.');
const firstHold = pixels('ending.mp4',60), lastHold = pixels('ending.mp4',299);
let deviation=0;
for(let i=0;i<firstHold.length;i++) deviation+=Math.abs(firstHold[i]-lastHold[i]);
deviation/=firstHold.length;
assert.ok(deviation<.2,`Title must hold still; codec mean deviation ${deviation}`);
writeFileSync(resolve(output,'verification.json'),JSON.stringify({passed:true,frameCounts:true,alphaBoundaryFrames:[0,89,90,119],subtitleSafeArea:true,titleMeanCodecDeviation:deviation},null,2));
console.log(`PASS graphics: decoded alpha boundaries, frame counts, subtitle safe area, title hold. ${output}`);

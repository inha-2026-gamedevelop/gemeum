import {mkdirSync, writeFileSync, readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import assert from 'node:assert/strict';
import {masterAudio} from './master-audio.mjs';
import {inspectMaster} from './inspect-master.mjs';

const root = fileURLToPath(new URL('../',import.meta.url));
const output = resolve(root,'out',`verify-master-${randomUUID()}`);
mkdirSync(output,{recursive:true});
const source = resolve(output,'synthetic-73s.mp4');
const result = spawnSync('ffmpeg',['-hide_banner','-loglevel','error','-f','lavfi','-i','color=black:s=1920x1080:r=60:d=73',
  '-f','lavfi','-i',"aevalsrc='if(between(t,48,58),0.008,0.2)*sin(2*PI*440*t)':s=48000:d=73",'-c:v','libx264','-preset','ultrafast','-crf','30','-c:a','aac',source],
  {encoding:'utf8',windowsHide:true,maxBuffer:8*1024*1024});
if (result.error || result.status !== 0) throw new Error(result.error?.message ?? result.stderr);
const destination = resolve(output,'master.mp4');
assert.equal(masterAudio(source,destination).audioPassed,true);
const inspection = inspectMaster(destination);
assert.equal(inspection.technicalPassed,true,JSON.stringify(inspection));
assert.ok(inspection.dropLu >= 15);
const reportBefore = readFileSync(`${destination}.loudness.json`,'utf8');
assert.throws(() => masterAudio(source,destination),/new file/);
assert.equal(readFileSync(`${destination}.loudness.json`,'utf8'),reportBefore);
const silence = resolve(output,'silence.wav');
const silent = spawnSync('ffmpeg',['-hide_banner','-loglevel','error','-f','lavfi','-i','anullsrc=r=48000:cl=stereo','-t','3',silence],{encoding:'utf8',windowsHide:true});
if (silent.status !== 0) throw new Error(silent.stderr);
assert.throws(() => masterAudio(silence,resolve(output,'must-not-pass.wav')),/silent/);
writeFileSync(resolve(output,'result.json'),JSON.stringify({syntheticOnly:true,passed:true,inspection,overwriteRefused:true,silenceRefused:true},null,2));
console.log(`PASS mastering / post-AAC inspection / contrast / overwrite / silence: ${output}`);

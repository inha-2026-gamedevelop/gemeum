import {readFileSync, writeFileSync, renameSync, unlinkSync, existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
import {parseArgs} from 'node:util';
import {randomUUID} from 'node:crypto';
import {validatePlan} from '../src/plan.mjs';
import {localMedia, probeMedia, videoMetadata, verifyVideo, verifyAudio} from './media.mjs';

// Only updates editorial metadata. Original files are never copied, moved or recorded.
export function registerMedia(values, root = fileURLToPath(new URL('../../../', import.meta.url))) {
  if (!!values.shot === !!values.audio || !values.file) throw new Error('Use --shot ID --take ID --file media/file.mp4 or --audio ID --file media/file.wav [--source-in FRAMES].');
  const path = resolve(root, 'Assets/09.Data/Teaser/teaser-shots.json');
  const original = readFileSync(path, 'utf8');
  const plan = JSON.parse(original);
  const initialErrors = validatePlan(plan);
  if (initialErrors.length) throw new Error(initialErrors.join('\n'));
  const probe = probeMedia(localMedia(resolve(root, 'tools/teaser/public'), values.file));
  const sourceInFrame = values['source-in'] === undefined ? undefined : Number(values['source-in']);
  if (sourceInFrame !== undefined && (!Number.isSafeInteger(sourceInFrame) || sourceInFrame < 0)) throw new Error('source-in must be a non-negative integer.');
  if (values.shot) {
    const shot = plan.shots.find(s => s.id === values.shot);
    if (!shot || shot.kind === 'title' || !values.take) throw new Error('A non-title shot and --take ID are required.');
    if (shot.takes.some(t => t.id === values.take)) throw new Error('Take ID already exists. Use a new ID to preserve previous takes.');
    if (sourceInFrame !== undefined) shot.sourceInFrame = sourceInFrame;
    const take = {id: values.take, path: values.file, ...videoMetadata(probe, plan), origin: 'unity-recorder'};
    verifyVideo(probe, plan, shot, take);
    shot.takes.push(take);
    shot.selectedTakeId = take.id;
  } else {
    const cue = plan.audioCues.find(c => c.id === values.audio);
    if (!cue) throw new Error('Unknown audio cue.');
    cue.assetPath = values.file;
    if (sourceInFrame !== undefined) cue.sourceInFrame = sourceInFrame;
    verifyAudio(probe, plan, cue);
  }
  const errors = validatePlan(plan);
  if (errors.length) throw new Error(errors.join('\n'));
  const temporary = `${path}.${randomUUID()}.tmp`;
  try {
    writeFileSync(temporary, `${JSON.stringify(plan, null, 2)}\n`, {flag: 'wx'});
    if (readFileSync(path, 'utf8') !== original) throw new Error('Plan changed externally. Reload before registering.');
    renameSync(temporary, path);
  } finally {
    if (existsSync(temporary)) unlinkSync(temporary);
  }
  return `Registered ${values.shot ?? values.audio}: ${values.file}. Preparation status remains unchanged.`;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const {values} = parseArgs({options: {shot: {type: 'string'}, audio: {type: 'string'}, file: {type: 'string'}, take: {type: 'string'}, 'source-in': {type: 'string'}}});
    console.log(registerMedia(values));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

import {readFileSync, existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
import {validatePlan, readiness} from '../src/plan.mjs';
import {localMedia, probeMedia, verifyVideo, verifyAudio} from './media.mjs';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const path = resolve(root, 'Assets/09.Data/Teaser/teaser-shots.json');
try {
  const plan = JSON.parse(readFileSync(path, 'utf8'));
  const errors = validatePlan(plan);
  const inspectMedia = process.argv.includes('--media') || process.argv.includes('--ready');
  const probes = new Map();
  const inspect = path => {
    const file = localMedia(resolve(root, 'tools/teaser/public'), path);
    if (!probes.has(file)) probes.set(file, probeMedia(file));
    return probes.get(file);
  };
  if (!errors.length) {
    for (const s of plan.shots) {
      if (s.scene && !existsSync(resolve(root, s.scene))) errors.push(`Scene not found: ${s.scene}`);
      if (s.selectedTakeId !== null) {
        const take = s.takes.find(t => t.id === s.selectedTakeId);
        if (!existsSync(resolve(root, 'tools/teaser/public', take.path))) errors.push(`Selected media not found: ${take.path}`);
        else if (inspectMedia) {
          try { verifyVideo(inspect(take.path), plan, s, take); }
          catch (error) { errors.push(`${s.id}: ${error.message}`); }
        }
      }
    }
    for (const cue of plan.audioCues) {
      if (cue.assetPath !== null && !existsSync(resolve(root, 'tools/teaser/public', cue.assetPath))) errors.push(`Audio not found: ${cue.assetPath}`);
      else if (cue.assetPath !== null && inspectMedia) {
        try { verifyAudio(inspect(cue.assetPath), plan, cue); }
        catch (error) { errors.push(`${cue.id}: ${error.message}`); }
      }
    }
  }
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else {
    console.log(`VALID PLAN: ${plan.shots.length} shots / ${plan.totalFrames} frames / ${plan.totalFrames / plan.fps}s`);
    const pending = readiness(plan).filter(s => s.missing.length);
    console.log(`PREPARATION ONLY: ${pending.length} items need preparation or source media.`);
    if (process.argv.includes('--ready') && pending.length) { console.error(pending); process.exitCode = 1; }
  }
} catch (error) { console.error(error.message); process.exitCode = 1; }

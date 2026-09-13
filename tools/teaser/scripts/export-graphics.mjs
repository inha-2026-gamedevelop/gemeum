import {mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {bundle} from '@remotion/bundler';
import {getCompositions, renderMedia, renderStill} from '@remotion/renderer';
import {captionFile} from '../src/editorial.mjs';
import {validatePlan} from '../src/plan.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const plan = JSON.parse(readFileSync(resolve(root, '../../Assets/09.Data/Teaser/teaser-shots.json'), 'utf8'));
const errors = validatePlan(plan);
if (errors.length) throw new Error(errors.join('\n'));
const output = resolve(root, 'out/graphics');
mkdirSync(output, {recursive: true});
const serveUrl = await bundle({entryPoint: resolve(root, 'src/index.jsx'), outDir: resolve(root, 'build/graphics'), publicDir: resolve(root, 'public')});
const compositions = await getCompositions(serveUrl);
const specs = [
  {id: 'TeaserEnding', file: 'ending.mp4', codec: 'h264', frame: 120},
  {id: 'TeaserSubtitle', file: 'subtitle-alpha.mov', codec: 'prores', frame: 100},
  {id: 'TeaserInkTransition', file: 'ink-transition-alpha.mov', codec: 'prores', frame: 40},
  {id: 'TeaserGraphics', file: 'preview.mp4', codec: 'h264', frame: 280},
];
for (const spec of specs) {
  const composition = compositions.find(c => c.id === spec.id);
  console.log(`Exporting ${spec.file}: ${composition.durationInFrames} frames`);
  await renderMedia({serveUrl, composition, codec: spec.codec, outputLocation: resolve(output, spec.file),
    overwrite: true, concurrency: 2, ...(spec.codec === 'prores' ? {proResProfile: '4444', pixelFormat: 'yuva444p10le', imageFormat: 'png'} : {crf: 18, pixelFormat: 'yuv420p'})});
  await renderStill({serveUrl, composition, frame: spec.frame, output: resolve(output, `${spec.id}.png`), imageFormat: 'png'});
}
writeFileSync(resolve(output, 'subtitles.srt'), captionFile(plan));
writeFileSync(resolve(output, 'subtitles.vtt'), captionFile(plan, 'vtt'));
writeFileSync(resolve(output, 'manifest.json'), JSON.stringify({origin: 'editorial-graphics-not-game-footage', fps: plan.fps, width: plan.width, height: plan.height,
  font: 'Project Hangeuljaemin4-Regular.ttf (Kookmin University)', files: specs.map(s => ({...s, frames: compositions.find(c => c.id === s.id).durationInFrames}))}, null, 2));
console.log(`Graphics exported: ${output}`);

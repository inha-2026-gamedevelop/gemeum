import {spawnSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseArgs} from 'node:util';
import {probeMedia, videoMetadata} from './media.mjs';
import {readLoudness} from './master-audio.mjs';

function measure(input, start, duration) {
  const trim = start === undefined ? '' : `atrim=start=${start}:duration=${duration},asetpts=PTS-STARTPTS,`;
  const result = spawnSync('ffmpeg', ['-hide_banner', '-nostdin', '-i', input,
    '-vn', '-af', `${trim}loudnorm=I=-14:TP=-1:LRA=20:print_format=json`, '-f', 'null', '-'],
  {encoding: 'utf8', windowsHide: true, maxBuffer: 16*1024*1024});
  if (result.error || result.status !== 0) throw new Error(result.error?.message ?? result.stderr);
  return readLoudness(result.stderr);
}

export function inspectMaster(input) {
  input = resolve(input);
  const failures = [];
  const probe = probeMedia(input);
  try {
    const video = videoMetadata(probe, {width: 1920, height: 1080, fps: 60});
    if (video.frameCount < 4320 || video.frameCount > 4380) failures.push('Video must be 4320..4380 frames.');
  } catch (error) { failures.push(error.message); }
  if (probe.streams?.find(s => s.codec_type === 'video')?.codec_name !== 'h264') failures.push('Video codec must be H.264.');
  let full, before, after, dropLu;
  try {
    full = measure(input);
    if (Math.abs(Number(full.input_i)+14) > .5) failures.push('Integrated loudness must be -14 ±0.5 LUFS.');
    if (Number(full.input_tp) > -1) failures.push('True peak must be <= -1 dBTP.');
    // Equal 3-second windows, each measured independently; do not mix gated full-file IL with window IL.
    before = measure(input,45,3);
    after = measure(input,48,3);
    dropLu = Number(before.input_i)-Number(after.input_i);
    if (dropLu < 15) failures.push('48s contrast must drop at least 15 LU (45..48 vs 48..51 seconds).');
  } catch (error) { failures.push(error.message); }
  const report = {input, technicalPassed: failures.length === 0, failures, full, before, after, dropLu,
    pictureAndRightsReviewed: false, contrastMetric: 'independently measured 3-second integrated loudness'};
  writeFileSync(`${input}.inspection.json`, JSON.stringify(report,null,2));
  return report;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const {values} = parseArgs({options: {input: {type: 'string'}}});
    if (!values.input) throw new Error('Use --input master.mp4');
    const result = inspectMaster(values.input); console.log(JSON.stringify(result,null,2));
    if (!result.technicalPassed) process.exitCode = 1;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

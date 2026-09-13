import {spawnSync} from 'node:child_process';
import {existsSync, writeFileSync} from 'node:fs';
import {resolve, extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseArgs} from 'node:util';

function run(args) {
  const result = spawnSync('ffmpeg', ['-hide_banner', '-nostdin', ...args], {encoding: 'utf8', windowsHide: true, maxBuffer: 16*1024*1024});
  if (result.error || result.status !== 0) throw new Error(`ffmpeg failed: ${result.error?.message ?? result.stderr}`);
  return result.stderr;
}

export function readLoudness(log) {
  const blocks = log.match(/\{\s*"input_i"[\s\S]*?\}/g);
  if (!blocks?.length) throw new Error('Loudness measurement missing.');
  const data = JSON.parse(blocks.at(-1));
  for (const key of ['input_i', 'input_tp', 'input_lra', 'input_thresh', 'target_offset']) {
    if (!Number.isFinite(Number(data[key]))) throw new Error('Audio is silent or cannot be normalized.');
  }
  return data;
}

export function masterAudio(input, output) {
  input = resolve(input); output = resolve(output);
  if (!existsSync(input) || existsSync(output)) throw new Error('Input must exist and output must be a new file.');
  const extension = extname(output).toLowerCase();
  if (!['.wav', '.mp4'].includes(extension)) throw new Error('Output must be .wav (audio) or .mp4 (video/audio).');
  const target = 'I=-14:TP=-1.5:LRA=20';
  const first = readLoudness(run(['-i', input, '-vn', '-af', `loudnorm=${target}:print_format=json`, '-f', 'null', '-']));
  const filter = `loudnorm=${target}:measured_I=${first.input_i}:measured_TP=${first.input_tp}:measured_LRA=${first.input_lra}:measured_thresh=${first.input_thresh}:offset=${first.target_offset}:linear=true:print_format=json`;
  const encoding = extension === '.wav' ? ['-vn', '-c:a', 'pcm_s24le'] : ['-map', '0:v:0', '-map', '0:a:0', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '320k', '-movflags', '+faststart'];
  const second = readLoudness(run(['-n', '-i', input, ...encoding, '-af', filter, '-ar', '48000', output]));
  const final = readLoudness(run(['-i', output, '-vn', '-af', `loudnorm=${target}:print_format=json`, '-f', 'null', '-']));
  const passed = Math.abs(Number(final.input_i) + 14) <= .5 && Number(final.input_tp) <= -1;
  const report = {input, output, target: {integratedLufs: -14, maximumTruePeakDbtp: -1}, first, normalization: second.normalization_type, final,
    audioPassed: passed, pictureAndRightsReviewed: false};
  writeFileSync(`${output}.loudness.json`, JSON.stringify(report, null, 2));
  if (!passed) throw new Error(`Final encoded audio misses target; inspect ${output}.loudness.json. Output retained for diagnosis.`);
  return report;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const {values} = parseArgs({options: {input: {type: 'string'}, output: {type: 'string'}}});
    if (!values.input || !values.output) throw new Error('Use --input mix.wav --output master.wav (or .mp4).');
    console.log(JSON.stringify(masterAudio(values.input, values.output), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

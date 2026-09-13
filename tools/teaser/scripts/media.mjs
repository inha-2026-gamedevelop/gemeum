import {spawnSync} from 'node:child_process';
import {realpathSync, statSync} from 'node:fs';
import {resolve, relative, isAbsolute, sep} from 'node:path';
import {audioSettings} from '../src/plan.mjs';

export function localMedia(publicRoot, path) {
  if (typeof path !== 'string' || !/^media\/[A-Za-z0-9_./-]+$/.test(path) || path.split('/').includes('..')) throw new Error(`Unsafe media path: ${path}`);
  const root = realpathSync(resolve(publicRoot, 'media'));
  const file = realpathSync(resolve(publicRoot, path));
  const child = relative(root, file);
  if (!child || child === '..' || child.startsWith(`..${sep}`) || isAbsolute(child) || !statSync(file).isFile()) throw new Error(`Media must be a file within public/media: ${path}`);
  return file;
}

export function probeMedia(path) {
  const result = spawnSync('ffprobe', ['-v', 'error', '-count_frames', '-show_streams', '-show_format', '-of', 'json', path],
    {encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 8 * 1024 * 1024});
  if (result.error || result.status !== 0) throw new Error(`ffprobe failed: ${result.error?.message ?? result.stderr}`);
  return JSON.parse(result.stdout);
}

const rate = value => {
  const [n, d = '1'] = String(value).split('/');
  return Number(n) / Number(d);
};

export function videoMetadata(probe, plan) {
  const stream = probe.streams?.find(s => s.codec_type === 'video' && !s.disposition?.attached_pic);
  if (!stream) throw new Error('Video stream missing.');
  if (stream.width !== plan.width || stream.height !== plan.height) throw new Error('Video resolution must match the plan.');
  if (Math.abs(rate(stream.avg_frame_rate) - plan.fps) > 0.00001 || Math.abs(rate(stream.r_frame_rate) - plan.fps) > 0.00001) throw new Error('Video fps must match the plan; normalize variable frame rate sources first.');
  const frameCount = Number(stream.nb_read_frames);
  if (!Number.isSafeInteger(frameCount) || frameCount <= 0) throw new Error('Decoded frame count unavailable.');
  return {fps: plan.fps, frameCount};
}

export function verifyVideo(probe, plan, shot, take) {
  const actual = videoMetadata(probe, plan);
  if (actual.frameCount !== take.frameCount || actual.fps !== take.fps) throw new Error(`Take metadata differs from file: ${take.id}`);
  if (actual.frameCount < shot.sourceInFrame + shot.durationFrames) throw new Error(`Video too short: ${shot.id}`);
}

export function verifyAudio(probe, plan, cue) {
  const stream = probe.streams?.find(s => s.codec_type === 'audio');
  if (!stream) throw new Error(`Audio stream missing: ${cue.id}`);
  const duration = Number(stream.duration ?? probe.format?.duration);
  const required = (audioSettings(cue).sourceInFrame + cue.durationFrames) / plan.fps;
  if (!Number.isFinite(duration) || duration < required) throw new Error(`Audio too short: ${cue.id} needs ${required}s`);
}

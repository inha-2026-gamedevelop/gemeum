// Shared by CLI validation and the Remotion preview; frame ranges are [start, end)
export function validatePlan(plan) {
  const errors = [];
  const check = (condition, message) => { if (!condition) errors.push(message); };
  const integer = value => Number.isSafeInteger(value) && value >= 0;
  const text = value => typeof value === 'string' && value.trim().length > 0;
  if (!plan || typeof plan !== 'object') return ['Plan must be an object.'];
  check(plan.schemaVersion === 1, 'schemaVersion must be 1.');
  check(plan.fps === 60 && plan.width === 1920 && plan.height === 1080, 'Output must be 1920x1080 at 60 fps.');
  check(integer(plan.totalFrames) && plan.totalFrames >= 4320 && plan.totalFrames <= 4380, 'Duration must be 4320..4380 frames (72..73 seconds).');
  const ids = new Set();
  const unique = (id, scope) => { check(text(id) && !ids.has(`${scope}:${id}`), `Duplicate or missing ${scope} id: ${id}`); ids.add(`${scope}:${id}`); };
  const list = (value, label) => { check(Array.isArray(value), `${label} must be an array.`); return Array.isArray(value) ? value : []; };
  const requirements = list(plan.requirements, 'requirements');
  for (const r of requirements) {
    if (!r) { errors.push('Null requirement.'); continue; }
    unique(r.id, 'requirement');
    check(['pending', 'blocked', 'ready'].includes(r.status), `Invalid requirement status: ${r.id}`);
  }
  const range = (item, label) => {
    check(integer(item.startFrame) && integer(item.durationFrames) && item.durationFrames > 0 && item.startFrame + item.durationFrames <= plan.totalFrames, `Invalid frame range: ${label}`);
  };
  const shots = list(plan.shots, 'shots');
  check(shots.length > 0, 'At least one shot is required.');
  let end = 0;
  for (const s of shots) {
    if (!s) { errors.push('Null shot.'); continue; }
    unique(s.id, 'shot'); range(s, s.id);
    check(s.startFrame === end, `Gap or overlap before ${s.id}: expected ${end}, got ${s.startFrame}`);
    end = s.startFrame + s.durationFrames;
    check(text(s.label), `Missing shot label: ${s.id}`);
    check(['pending', 'ready'].includes(s.setupStatus), `Invalid setupStatus: ${s.id}`);
    check([1, 2, 3].includes(s.act), `Invalid act: ${s.id}`);
    check(['gameplay', 'presentation', 'title'].includes(s.kind), `Invalid shot kind: ${s.id}`);
    check(s.kind === 'title' || (typeof s.scene === 'string' && /^Assets\/00\.Scenes\/.+\.unity$/.test(s.scene) && !s.scene.includes('..')), `Invalid scene: ${s.id}`);
    check(Number.isFinite(s.timeScale) && s.timeScale > 0 && s.timeScale <= 3, `Invalid timeScale: ${s.id}`);
    check(s.kind !== 'gameplay' || s.timeScale === 1, `Gameplay must use timeScale 1: ${s.id}`);
    for (const field of ['sourceInFrame', 'handleFrames', 'warmupFrames', 'seed']) check(integer(s[field]), `Invalid ${field}: ${s.id}`);
    for (const key of ['cameraFrom', 'cameraTo']) {
      const pose = s[key];
      if (pose !== null) check(pose && ['position', 'euler'].every(k => Array.isArray(pose[k]) && pose[k].length === 3 && pose[k].every(Number.isFinite)) && Number.isFinite(pose.fov) && pose.fov > 0 && pose.fov < 180, `Invalid ${key}: ${s.id}`);
    }
    for (const req of list(s.requirements, `${s.id}.requirements`)) check(requirements.some(r => r?.id === req), `Unknown requirement ${req}: ${s.id}`);
    for (const trigger of list(s.triggers, `${s.id}.triggers`)) check(trigger && text(trigger.kind) && trigger.clock === 'output' && integer(trigger.offsetFrame) && trigger.offsetFrame < s.durationFrames, `Invalid trigger/output clock: ${s.id}`);
    const takes = list(s.takes, `${s.id}.takes`);
    const takeIds = new Set();
    for (const take of takes) {
      if (!take) { errors.push(`Null take: ${s.id}`); continue; }
      check(text(take.id) && !takeIds.has(take.id), `Duplicate/missing take: ${s.id}`); takeIds.add(take.id);
      check(typeof take.path === 'string' && /^media\/[A-Za-z0-9_./-]+$/.test(take.path) && !take.path.split('/').includes('..'), `Unsafe media path: ${s.id}`);
      check(take.fps === plan.fps && integer(take.frameCount) && take.frameCount >= s.sourceInFrame + s.durationFrames, `Insufficient take frames/fps: ${s.id}`);
      check(take.origin === 'unity-recorder', `Game image must originate in Unity Recorder: ${s.id}`);
    }
    check(s.selectedTakeId === null || takes.some(t => t?.id === s.selectedTakeId), `Unknown selected take: ${s.id}`);
  }
  check(end === plan.totalFrames, `Shot end ${end} does not match totalFrames ${plan.totalFrames}.`);
  for (const o of list(plan.overlays, 'overlays')) {
    if (!o) { errors.push('Null overlay.'); continue; }
    unique(o.id, 'overlay'); range(o, o.id);
    check(['subtitle', 'title', 'transition-guide'].includes(o.kind) && text(o.text), `Invalid overlay: ${o.id}`);
    if (o.sourceShotId || o.targetShotId) {
      const a = shots.find(s => s?.id === o.sourceShotId), b = shots.find(s => s?.id === o.targetShotId);
      check(a && b && a.startFrame+a.durationFrames === b.startFrame && o.startFrame >= a.startFrame && o.startFrame+o.durationFrames <= b.startFrame+b.durationFrames && o.startFrame <= b.startFrame && o.startFrame+o.durationFrames >= b.startFrame, `Transition must straddle adjacent shots: ${o.id}`);
    }
  }
  for (const cue of list(plan.audioCues, 'audioCues')) {
    if (!cue) { errors.push('Null audio cue.'); continue; }
    unique(cue.id, 'audio'); range(cue, cue.id);
    check(text(cue.label), `Missing audio cue label: ${cue.id}`);
    check(cue.assetPath === null || (typeof cue.assetPath === 'string' && /^media\/[A-Za-z0-9_./-]+$/.test(cue.assetPath) && !cue.assetPath.split('/').includes('..')), `Unsafe audio path: ${cue.id}`);
    const mix = audioSettings(cue);
    check(integer(mix.sourceInFrame), `Invalid audio sourceInFrame: ${cue.id}`);
    check(Number.isFinite(mix.volume) && mix.volume >= 0 && mix.volume <= 1, `Invalid audio volume: ${cue.id}`);
    check(integer(mix.fadeInFrames) && integer(mix.fadeOutFrames) && mix.fadeInFrames + mix.fadeOutFrames <= cue.durationFrames, `Invalid audio fades: ${cue.id}`);
  }
  return errors;
}

export function audioSettings(cue) {
  return {sourceInFrame: cue.sourceInFrame ?? 0, volume: cue.volume ?? 1,
    fadeInFrames: cue.fadeInFrames ?? 0, fadeOutFrames: cue.fadeOutFrames ?? 0};
}

// Frame passed here is relative to the cue, after trimming the source
export function audioGain(cue, localFrame) {
  if (localFrame < 0 || localFrame >= cue.durationFrames) return 0;
  const mix = audioSettings(cue);
  const fadeIn = mix.fadeInFrames ? Math.min(1, localFrame / mix.fadeInFrames) : 1;
  const fadeOut = mix.fadeOutFrames ? Math.min(1, (cue.durationFrames - 1 - localFrame) / mix.fadeOutFrames) : 1;
  return mix.volume * Math.min(fadeIn, fadeOut);
}

export function activeAt(items, frame) {
  return items.filter(item => frame >= item.startFrame && frame < item.startFrame + item.durationFrames);
}

export function readiness(plan) {
  return [...plan.shots.map(s => ({id: s.id, missing: [
    ...(s.setupStatus !== 'ready' ? ['배우·환경 초기 상태 미확정'] : []),
    ...(s.kind !== 'title' && s.cameraFrom === null ? ['시작 카메라 미설정'] : []),
    ...(s.kind !== 'title' && s.cameraTo === null ? ['종료 카메라 미설정'] : []),
    ...(s.kind !== 'title' && s.selectedTakeId === null ? ['녹화 원본 없음'] : []),
    ...s.requirements.filter(id => plan.requirements.find(r => r.id === id)?.status !== 'ready'),
  ]})), ...plan.audioCues.map(cue => ({id: `audio:${cue.id}`, missing: cue.assetPath === null ? ['음원 원본 없음'] : []}))];
}

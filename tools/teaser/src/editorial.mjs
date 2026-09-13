export const editorial = {
  ink: '#080b0c', paper: '#e8e2d4', edge: '#54221f', seed: 913,
  titleFontSize: 244, subtitleFontSize: 48, subtitleBottom: 142,
};

export function inkAmount(overlay, frame, plan) {
  const local = frame - overlay.startFrame;
  if (local < 0 || local >= overlay.durationFrames) return 0;
  if (!overlay.targetShotId) return local / (overlay.durationFrames - 1);
  const boundary = plan.shots.find(s => s.id === overlay.targetShotId).startFrame - overlay.startFrame;
  // Entire image is covered on both sides of the cut; output duration never shrinks.
  if (local < boundary) return Math.min(1, local / Math.max(1, boundary - 1));
  return Math.max(0, 1 - (local - boundary) / Math.max(1, overlay.durationFrames - 1 - boundary));
}

export function subtitleOpacity(overlay, frame) {
  const local = frame - overlay.startFrame;
  const fade = Math.min(8, Math.floor(overlay.durationFrames / 4));
  if (local < 0 || local >= overlay.durationFrames) return 0;
  return Math.min(1, local / Math.max(1, fade), (overlay.durationFrames - 1 - local) / Math.max(1, fade));
}

export function captionFile(plan, format = 'srt') {
  const stamp = frame => {
    const ms = Math.round(frame * 1000 / plan.fps);
    return `${String(Math.floor(ms/3600000)).padStart(2,'0')}:${String(Math.floor(ms/60000)%60).padStart(2,'0')}:${String(Math.floor(ms/1000)%60).padStart(2,'0')}${format === 'vtt' ? '.' : ','}${String(ms%1000).padStart(3,'0')}`;
  };
  const blocks = plan.overlays.filter(o => o.kind === 'subtitle').map((o,i) => `${i+1}\n${stamp(o.startFrame)} --> ${stamp(o.startFrame+o.durationFrames)}\n${o.text}`);
  return `${format === 'vtt' ? 'WEBVTT\n\n' : ''}${blocks.join('\n\n')}\n`;
}

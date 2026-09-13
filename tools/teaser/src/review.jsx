import React from 'react';
import {AbsoluteFill, Audio, OffthreadVideo, Sequence, staticFile, useCurrentFrame} from 'remotion';
import {activeAt, audioGain, audioSettings, validatePlan} from './plan.mjs';
import {EditorialLayers} from './graphics.jsx';

function SoundCue({cue}) {
  const frame = useCurrentFrame();
  const mix = audioSettings(cue);
  return <Audio src={staticFile(cue.assetPath)} trimBefore={mix.sourceInFrame} volume={audioGain(cue, frame)} />;
}

export function TeaserReview({plan}) {
  const frame = useCurrentFrame();
  const errors = validatePlan(plan);
  if (errors.length) throw new Error(errors.join('\n'));
  const shot = activeAt(plan.shots, frame)[0];
  const take = shot.takes.find(t => t.id === shot.selectedTakeId);
  const missingAudio = activeAt(plan.audioCues, frame).filter(c => !c.assetPath);
  return <AbsoluteFill style={{backgroundColor: '#101719', color: '#eeeae3', fontFamily: 'Malgun Gothic, sans-serif'}}>
    {plan.shots.filter(s => s.kind !== 'title').map(s => {
      const media = s.takes.find(t => t.id === s.selectedTakeId);
      return media && <Sequence key={s.id} from={s.startFrame} durationInFrames={s.durationFrames} name={s.label}>
        <OffthreadVideo src={staticFile(media.path)} trimBefore={s.sourceInFrame} muted style={{width: '100%', height: '100%', objectFit: 'contain'}} />
      </Sequence>;
    })}
    {!take && shot.kind !== 'title' && <AbsoluteFill style={{justifyContent: 'center', alignItems: 'center', gap: 30}}>
      <div style={{fontSize: 54}}>{shot.label}</div>
      <div style={{fontSize: 30, color: '#d4b381'}}>녹화 원본 미등록</div>
    </AbsoluteFill>}
    {plan.audioCues.filter(c => c.assetPath).map(cue => <Sequence key={cue.id} from={cue.startFrame} durationInFrames={cue.durationFrames} layout="none" name={cue.label}>
      <SoundCue cue={cue} />
    </Sequence>)}
    <EditorialLayers plan={plan} frame={frame} />
    <div style={{position: 'absolute', top: 30, left: 40, right: 40, display: 'flex', justifyContent: 'space-between', fontSize: 24, textShadow: '0 2px 4px black'}}>
      <span>그믐 · 원본 조립 검토 · 미완성</span><span>{shot.id} · {frame}f</span>
    </div>
    <div style={{position: 'absolute', bottom: 30, left: 40, right: 40, fontSize: 23, textShadow: '0 2px 4px black'}}>
      {missingAudio.length > 0 && <div>음원 미등록: {missingAudio.map(c => c.label).join(' / ')}</div>}
    </div>
  </AbsoluteFill>;
}

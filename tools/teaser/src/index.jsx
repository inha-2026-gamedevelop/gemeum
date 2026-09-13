import React from 'react';
import {AbsoluteFill, Composition, registerRoot, useCurrentFrame} from 'remotion';
import plan from '../../../Assets/09.Data/Teaser/teaser-shots.json';
import {activeAt, validatePlan} from './plan.mjs';
import {TeaserReview} from './review.jsx';
import {GraphicsExport, GraphicsPreview} from './graphics.jsx';

const errors = validatePlan(plan);
if (errors.length) throw new Error(errors.join('\n'));
const palette = {1: ['#181716', '#d6b37b'], 2: ['#151a20', '#adb8c2'], 3: ['#101d20', '#8dd5ca']};
const timecode = frame => `${Math.floor(frame / plan.fps).toString().padStart(2, '0')}:${(frame % plan.fps).toString().padStart(2, '0')}`;

function TeaserPlan() {
  const frame = useCurrentFrame();
  const shot = activeAt(plan.shots, frame)[0];
  const overlays = activeAt(plan.overlays, frame);
  const audio = activeAt(plan.audioCues, frame);
  const [background, accent] = palette[shot.act];
  const title = overlays.find(o => o.kind === 'title');
  const subtitle = overlays.find(o => o.kind === 'subtitle');
  const guide = overlays.find(o => o.kind === 'transition-guide');
  const progress = (frame - shot.startFrame) / shot.durationFrames;
  return <AbsoluteFill style={{background, color: '#eeeae3', fontFamily: 'Malgun Gothic, sans-serif', padding: 88}}>
    <div style={{display: 'flex', justifyContent: 'space-between', alignItems: 'center'}}>
      <div style={{fontSize: 24, letterSpacing: 6, color: accent}}>그믐 / 편집 준비</div>
      <div style={{fontSize: 24, color: '#c6c1b8'}}>샷 계획 미리보기 · 게임 화면 아님</div>
    </div>
    <div style={{marginTop: 100, color: accent, fontSize: 24}}>{shot.act}막 / {shot.id}</div>
    <div style={{fontSize: title ? 108 : 64, fontWeight: 600, marginTop: 34}}>{title?.text ?? shot.label}</div>
    <div style={{fontSize: 27, color: '#b4b6b3', marginTop: 32}}>
      {shot.kind === 'title' ? '타이틀 배치 · 최종 아트 미적용' : '카메라·배우 준비 후 Unity 원본으로 교체할 구간'}
    </div>
    <div style={{marginTop: 30, fontSize: 23, color: '#b4b6b3'}}>
      {shot.requirements.map(id => plan.requirements.find(r => r.id === id)?.note).join(' / ')}
    </div>
    {guide && <div style={{marginTop: 30, padding: 20, border: `1px solid ${accent}`, fontSize: 25}}>{guide.text} · 최종 마스크 아님</div>}
    {subtitle && <div style={{position: 'absolute', bottom: 260, left: 88, right: 88, textAlign: 'center', fontSize: 44}}>{subtitle.text}</div>}
    <div style={{position: 'absolute', left: 88, right: 88, bottom: 155, fontSize: 24, color: '#c6c1b8'}}>
      소리 계획: {audio.length ? audio.map(a => a.label).join(' / ') : '잔향·종료 믹스 준비'} · 음원 재생 없음
    </div>
    <div style={{position: 'absolute', bottom: 102, left: 88, right: 88, height: 5, background: '#354044'}}>
      <div style={{height: '100%', width: `${progress * 100}%`, background: accent}} />
    </div>
    <div style={{position: 'absolute', bottom: 53, left: 88, right: 88, display: 'flex', justifyContent: 'space-between', fontSize: 23}}>
      <span>{timecode(frame)} / {timecode(plan.totalFrames)} · 초:프레임</span>
      <span>{frame}f / {plan.totalFrames}f · {plan.fps}fps</span>
    </div>
  </AbsoluteFill>;
}

registerRoot(() => <>
  <Composition id="TeaserPlan" component={TeaserPlan} durationInFrames={plan.totalFrames} fps={plan.fps} width={plan.width} height={plan.height} />
  <Composition id="TeaserReview" component={TeaserReview} defaultProps={{plan}} durationInFrames={plan.totalFrames} fps={plan.fps} width={plan.width} height={plan.height} />
  <Composition id="TeaserEnding" component={GraphicsExport} defaultProps={{plan, section: 'ending'}} durationInFrames={plan.shots.find(s => s.kind === 'title').durationFrames} fps={plan.fps} width={plan.width} height={plan.height} />
  {['farewell', 'gat-match'].map(section => <Composition key={section} id={section === 'farewell' ? 'TeaserSubtitle' : 'TeaserInkTransition'} component={GraphicsExport} defaultProps={{plan, section}} durationInFrames={plan.overlays.find(o => o.id === section).durationFrames} fps={plan.fps} width={plan.width} height={plan.height} />)}
  <Composition id="TeaserGraphics" component={GraphicsPreview} defaultProps={{plan}} durationInFrames={plan.overlays.find(o => o.id === 'farewell').durationFrames + plan.overlays.find(o => o.id === 'gat-match').durationFrames + plan.shots.find(s => s.kind === 'title').durationFrames} fps={plan.fps} width={plan.width} height={plan.height} />
</>);

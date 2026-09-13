import React, {useEffect, useState} from 'react';
import {AbsoluteFill, cancelRender, continueRender, delayRender, useCurrentFrame} from 'remotion';
import fontUrl from '../../../Assets/13.UI/00.UI_Resource/Fonts/Hangeuljaemin4-Regular.ttf';
import {editorial, inkAmount, subtitleOpacity} from './editorial.mjs';

let fontPromise;
function useEditorialFont() {
  const [handle] = useState(() => delayRender('Loading project title font'));
  useEffect(() => {
    if (!fontPromise) fontPromise = new FontFace('GeumeumTitle', `url(${fontUrl})`).load().then(font => document.fonts.add(font));
    fontPromise.then(() => continueRender(handle)).catch(cancelRender);
  }, [handle]);
}

export function Ink({amount, id = 'ink'}) {
  if (amount <= 0) return null;
  if (amount >= 1) return <AbsoluteFill style={{backgroundColor: editorial.ink}} />;
  const p = amount * amount * (3 - 2 * amount);
  // Fixed irregular lobes and fixed turbulence seed: no flicker or random seek state.
  const centers = [[760,580,1], [1010,520,.91], [840,710,.72], [1190,610,.82], [590,440,.69]];
  return <svg viewBox="0 0 1920 1080" style={{position: 'absolute', width: '100%', height: '100%'}}>
    <defs><filter id={id} x="-30%" y="-50%" width="160%" height="200%" colorInterpolationFilters="sRGB">
      <feTurbulence type="fractalNoise" baseFrequency="0.012 0.024" numOctaves="3" seed={editorial.seed} result="noise" />
      <feDisplacementMap in="SourceGraphic" in2="noise" scale="75" xChannelSelector="R" yChannelSelector="G" />
    </filter></defs>
    <g filter={`url(#${id})`}>
      {centers.map(([x,y,weight], i) => <ellipse key={`edge-${i}`} cx={x} cy={y} rx={p*1500*weight+4} ry={p*1010*weight+4} fill={editorial.edge} opacity=".45" />)}
      {centers.map(([x,y,weight], i) => <ellipse key={i} cx={x} cy={y} rx={p*1500*weight} ry={p*1010*weight} fill={editorial.ink} />)}
    </g>
  </svg>;
}

export function TitleCard({text}) {
  useEditorialFont();
  const [name, hanja] = text.split(' ');
  return <AbsoluteFill style={{backgroundColor: editorial.ink, color: editorial.paper, alignItems: 'center', justifyContent: 'center'}}>
    <div style={{fontFamily: 'GeumeumTitle', fontSize: editorial.titleFontSize, letterSpacing: 35, paddingLeft: 35, lineHeight: 1.3}}>{name}</div>
    <div style={{width: 25, height: 2, backgroundColor: '#835147', marginTop: 38, marginBottom: 32}} />
    <div style={{fontFamily: 'GeumeumTitle, serif', fontSize: 44, letterSpacing: 9, paddingLeft: 9}}>{hanja}</div>
  </AbsoluteFill>;
}

export function Subtitle({overlay, frame}) {
  useEditorialFont();
  return <div style={{position: 'absolute', bottom: editorial.subtitleBottom, left: 120, right: 120,
    textAlign: 'center', fontFamily: 'GeumeumTitle', fontSize: editorial.subtitleFontSize,
    letterSpacing: 2, lineHeight: 1.5, color: editorial.paper, opacity: subtitleOpacity(overlay, frame),
    textShadow: '0 2px 3px #000, 0 0 12px #000, 0 0 26px #000'}}>{overlay.text}</div>;
}

// Reused unchanged in review, standalone alpha exports, and the preview reel.
export function EditorialLayers({plan, frame}) {
  const title = plan.overlays.find(o => o.kind === 'title' && frame >= o.startFrame && frame < o.startFrame+o.durationFrames);
  const endInk = plan.overlays.find(o => o.id === 'title-ink' && frame >= o.startFrame && frame < o.startFrame+o.durationFrames);
  return <>
    {endInk && <AbsoluteFill style={{backgroundColor: '#202625'}} />}
    {title && <TitleCard text={title.text} />}
    {plan.overlays.filter(o => o.kind === 'subtitle' && frame >= o.startFrame && frame < o.startFrame+o.durationFrames).map(o => <Subtitle key={o.id} overlay={o} frame={frame} />)}
    {plan.overlays.filter(o => o.kind === 'transition-guide').map(o => <Ink key={o.id} id={`ink-${o.id}`} amount={inkAmount(o, frame, plan)} />)}
  </>;
}

export function GraphicsExport({plan, section}) {
  const frame = useCurrentFrame();
  const overlay = plan.overlays.find(o => o.id === section);
  const start = section === 'ending' ? plan.shots.find(s => s.kind === 'title').startFrame : overlay.startFrame;
  return <EditorialLayers plan={plan} frame={start+frame} />;
}

export function GraphicsPreview({plan}) {
  const frame = useCurrentFrame();
  const subtitle = plan.overlays.find(o => o.kind === 'subtitle');
  const match = plan.overlays.find(o => o.id === 'gat-match');
  const titleShot = plan.shots.find(s => s.kind === 'title');
  const subtitleEnd = subtitle.durationFrames;
  const matchEnd = subtitleEnd + match.durationFrames;
  const outputFrame = frame < subtitleEnd ? subtitle.startFrame+frame : frame < matchEnd ? match.startFrame+frame-subtitleEnd : titleShot.startFrame+frame-matchEnd;
  const label = frame < subtitleEnd ? '자막 / 투명 오버레이' : frame < matchEnd ? '먹 전환 / 화면 밖 효과' : '엔딩 타이틀 / 4초 정지';
  return <AbsoluteFill style={{background: 'repeating-conic-gradient(#222a2c 0% 25%, #1c2325 0% 50%) 50% / 120px 120px'}}>
    <EditorialLayers plan={plan} frame={outputFrame} />
    <div style={{position: 'absolute', top: 35, left: 45, color: '#aaa', fontFamily: 'sans-serif', fontSize: 22}}>편집 요소 미리보기 · 게임 화면 없음 / {label}</div>
  </AbsoluteFill>;
}

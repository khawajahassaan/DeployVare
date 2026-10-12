'use client';

import { useEffect, useRef } from 'react';

// Animated login backdrop ported from AuditVare: drifting brand-colour blobs under a
// canvas of flowing lines, finished with a soft vignette and film grain. The dark variant
// keeps the same motion and brand hues on a deep navy-to-plum base, with brighter glows.

const GRAIN =
  "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E\")";

type Variant = 'light' | 'dark';

const THEMES = {
  light: {
    base: 'linear-gradient(135deg, #E9F2F9 0%, #EDEBF3 52%, #F8E6EA 100%)',
    blobs: ['rgba(15,108,189,0.30)', 'rgba(184,25,173,0.24)', 'rgba(82,11,77,0.20)'],
    ramp: [
      [15, 108, 189],
      [104, 74, 168],
      [184, 25, 173],
    ],
    lineAlpha: [0.16, 0.3],
    vignette: 'radial-gradient(120% 90% at 50% 42%, rgba(255,255,255,0) 38%, rgba(255,255,255,0.42) 100%)',
    grain: 0.05,
  },
  dark: {
    base: 'linear-gradient(135deg, #060A17 0%, #0F0B22 52%, #1A0819 100%)',
    blobs: ['rgba(15,108,189,0.55)', 'rgba(184,25,173,0.40)', 'rgba(104,74,168,0.45)'],
    ramp: [
      [64, 156, 236],
      [150, 118, 232],
      [228, 86, 212],
    ],
    lineAlpha: [0.2, 0.5],
    vignette: 'radial-gradient(120% 90% at 50% 42%, rgba(0,0,0,0) 36%, rgba(0,0,0,0.6) 100%)',
    grain: 0.07,
  },
} satisfies Record<Variant, unknown>;

const rampAt = (ramp: number[][], p: number, alpha: number) => {
  const scaled = Math.max(0, Math.min(1, p)) * (ramp.length - 1);
  const i = Math.min(ramp.length - 2, Math.floor(scaled));
  const f = scaled - i;
  const a = ramp[i];
  const b = ramp[i + 1];
  return `rgba(${Math.round(a[0] + (b[0] - a[0]) * f)},${Math.round(a[1] + (b[1] - a[1]) * f)},${Math.round(
    a[2] + (b[2] - a[2]) * f
  )},${alpha})`;
};

export default function AuroraBackdrop({ className = '', variant = 'light' }: { className?: string; variant?: Variant }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const theme = THEMES[variant];

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext('2d');
    if (!canvas || !ctx) return;

    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches;

    let t = 0;
    let rafId = 0;

    const resize = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = canvas.offsetWidth * dpr;
      canvas.height = canvas.offsetHeight * dpr;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    resize();
    window.addEventListener('resize', resize);

    const N = 16;

    const offsetAt = (nx: number, i: number, time: number) => {
      const phase = i * 0.28;
      const swell = Math.sin(time * 0.09 + phase * 0.5);
      const drift = Math.cos(time * 0.06 + phase * 0.7);

      const a1 = 46 + 20 * swell;
      const a2 = 22 + 12 * drift;
      const a3 = 11 + 6 * Math.sin(time * 0.13 + phase);

      const f1 = 1.5 + 0.5 * (0.5 + 0.5 * Math.sin(time * 0.05 + phase));
      const f2 = 2.7 + 0.6 * drift;

      return (
        Math.sin(nx * Math.PI * f1 + time * 0.34 + phase) * a1 +
        Math.sin(nx * Math.PI * f2 - time * 0.23 + phase * 1.2) * a2 +
        Math.cos(nx * Math.PI * 4.4 + time * 0.41 + phase * 0.8) * a3
      );
    };

    const draw = () => {
      const W = canvas.offsetWidth;
      const H = canvas.offsetHeight;
      ctx.clearRect(0, 0, W, H);

      // Short mobile banner: keep the waves readable and centred.
      // Tall desktop panel: weight them towards the lower part.
      const isCompact = H < 400;
      const fit = isCompact ? Math.min(1, Math.max(0.7, H / 320)) : Math.min(1, H / 900);
      const yMid = isCompact ? H * 0.5 : H * 0.62;
      const spread = isCompact ? H * 0.42 : H * 0.3;

      for (let i = 0; i < N; i++) {
        const p = i / (N - 1);
        const depth = Math.sin(p * Math.PI);
        const yCenter = yMid + (p - 0.5) * spread;

        ctx.beginPath();
        for (let x = -8; x <= W + 8; x += 2) {
          const nx = x / W;
          const y = yCenter + offsetAt(nx, i, t) * (0.5 + 0.7 * depth) * fit;
          if (x === -8) ctx.moveTo(x, y);
          else ctx.lineTo(x, y);
        }

        ctx.strokeStyle = rampAt(theme.ramp, p, theme.lineAlpha[0] + theme.lineAlpha[1] * depth);
        ctx.lineWidth = 0.7 + 0.9 * depth;
        ctx.stroke();
      }

      if (reduced) return;
      t += 0.01;
      rafId = requestAnimationFrame(draw);
    };

    draw();

    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener('resize', resize);
    };
  }, [theme]);

  return (
    <div className={`absolute inset-0 overflow-hidden rounded-[inherit] ${className}`} aria-hidden>
      <style>{`
        @keyframes dvAuroraA{0%,100%{transform:translate3d(-12%,-8%,0) scale(1)}50%{transform:translate3d(6%,10%,0) scale(1.18)}}
        @keyframes dvAuroraB{0%,100%{transform:translate3d(10%,6%,0) scale(1.1)}50%{transform:translate3d(-8%,-6%,0) scale(0.92)}}
        @keyframes dvAuroraC{0%,100%{transform:translate3d(0,12%,0) scale(1)}50%{transform:translate3d(12%,-4%,0) scale(1.22)}}
        @media (prefers-reduced-motion: reduce){
          .dv-aurora-blob{animation:none!important}
        }
      `}</style>

      <div
        className="absolute inset-0"
        style={{ background: theme.base }}
      />

      <div
        className="dv-aurora-blob absolute rounded-full"
        style={{
          width: '70%',
          aspectRatio: '1',
          left: '-10%',
          top: '-14%',
          background: `radial-gradient(circle, ${theme.blobs[0]} 0%, transparent 68%)`,
          filter: 'blur(46px)',
          animation: 'dvAuroraA 26s ease-in-out infinite',
        }}
      />
      <div
        className="dv-aurora-blob absolute rounded-full"
        style={{
          width: '62%',
          aspectRatio: '1',
          right: '-12%',
          top: '18%',
          background: `radial-gradient(circle, ${theme.blobs[1]} 0%, transparent 70%)`,
          filter: 'blur(52px)',
          animation: 'dvAuroraB 32s ease-in-out infinite',
        }}
      />
      <div
        className="dv-aurora-blob absolute rounded-full"
        style={{
          width: '78%',
          aspectRatio: '1',
          left: '8%',
          bottom: '-24%',
          background: `radial-gradient(circle, ${theme.blobs[2]} 0%, transparent 72%)`,
          filter: 'blur(58px)',
          animation: 'dvAuroraC 38s ease-in-out infinite',
        }}
      />

      <canvas ref={canvasRef} className="absolute inset-0 block size-full" />

      <div
        className="pointer-events-none absolute inset-0"
        style={{ background: theme.vignette }}
      />

      <div
        className="pointer-events-none absolute inset-0"
        style={{ backgroundImage: GRAIN, opacity: theme.grain, mixBlendMode: 'overlay' }}
      />
    </div>
  );
}

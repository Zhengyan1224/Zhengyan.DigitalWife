// Add this script to a host page; the player stays inside a transparent iframe.
(() => {
    const script = document.currentScript;
    if (!script) return;
    const number = (name, fallback) => {
        const value = Number(script.dataset[name]);
        return Number.isFinite(value) && value >= 0 ? value : fallback;
    };
    const player = new URL(script.dataset.src || './', script.src);
    player.searchParams.set('embed', '1');
    player.searchParams.set('desktopSprite', '1');
    const iframe = document.createElement('iframe');
    iframe.src = player.href;
    iframe.title = script.dataset.title || '网页宠物';
    iframe.dataset.webDesktopSprite = '';
    iframe.allow = 'autoplay; microphone; fullscreen';
    iframe.allowFullscreen = true;
    Object.assign(iframe.style, {
        position: 'fixed', right: `${number('right', 24)}px`, bottom: `${number('bottom', 0)}px`,
        width: `${Math.max(1, number('width', 360))}px`, height: `${Math.max(1, number('height', 480))}px`,
        maxWidth: '100vw', maxHeight: '100dvh', border: '0', background: 'transparent',
        boxShadow: 'none', zIndex: String(number('zIndex', 1000)),
        pointerEvents: script.dataset.interactive === 'false' ? 'none' : 'auto'
    });
    if (script.dataset.interactive === 'false') {
        iframe.tabIndex = -1;
        iframe.setAttribute('aria-hidden', 'true');
    }
    const mount = () => document.body.append(iframe);
    if (document.body) mount();
    else document.addEventListener('DOMContentLoaded', mount, { once: true });
})();

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
    let regions = null;
    let drag = null;
    const applyRegion = () => {
        if (drag) { iframe.style.clipPath=''; return; }
        if (!regions) { iframe.style.clipPath = ''; return; }
        const { width, height } = iframe.getBoundingClientRect();
        const path = regions.map(([x, y, w, h]) => {
            const left = Math.max(0, x) * width, top = Math.max(0, y) * height;
            const right = Math.min(1, x + w) * width, bottom = Math.min(1, y + h) * height;
            return `M${left.toFixed(2)} ${top.toFixed(2)}H${right.toFixed(2)}V${bottom.toFixed(2)}H${left.toFixed(2)}Z`;
        }).join('') || 'M0 0Z';
        iframe.style.clipPath = `path("${path}")`;
    };
    window.addEventListener('message', event => {
        if (event.source !== iframe.contentWindow || event.origin !== player.origin) return;
        if(event.data?.type==='zhengyan-desktop-sprite-host-query'||event.data?.type==='zhengyan-desktop-sprite-region')
            iframe.contentWindow?.postMessage({type:'zhengyan-desktop-sprite-host-ready'},player.origin);
        if(event.data?.type==='zhengyan-desktop-sprite-host-query')return;
        if(event.data?.type==='zhengyan-desktop-sprite-drag'){
            const value=event.data;
            if(value.phase==='end'){drag=null;applyRegion();return;}
            if(!Number.isFinite(value.x)||!Number.isFinite(value.y))return;
            if(value.phase==='begin'){
                const rect=iframe.getBoundingClientRect();drag={id:value.pointerId,x:value.x,y:value.y,left:rect.left,top:rect.top};
                Object.assign(iframe.style,{left:`${rect.left}px`,top:`${rect.top}px`,right:'auto',bottom:'auto'});applyRegion();
            }else if(value.phase==='move' && drag?.id===value.pointerId){
                const rect=iframe.getBoundingClientRect();
                iframe.style.left=`${Math.max(0,Math.min(window.innerWidth-rect.width,drag.left+value.x-drag.x))}px`;
                iframe.style.top=`${Math.max(0,Math.min(window.innerHeight-rect.height,drag.top+value.y-drag.y))}px`;
            }
            return;
        }
        if(event.data?.type !== 'zhengyan-desktop-sprite-region')return;
        const value = event.data;
        if (!Array.isArray(value.rects) || value.rects.length > 20000
            || value.rects.some(r => !Array.isArray(r) || r.length !== 4 || r.some(v => !Number.isFinite(v)) || r[2] < 0 || r[3] < 0)) return;
        regions = value.enabled ? value.rects : null;
        applyRegion();
    });
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(applyRegion).observe(iframe);
    window.addEventListener('resize', applyRegion, { passive: true });
    iframe.addEventListener('load',()=>iframe.contentWindow?.postMessage({type:'zhengyan-desktop-sprite-host-ready'},player.origin));
    window.addEventListener('blur',()=>{drag=null;applyRegion();});
    // A click in a clipped-out area reaches the host page, so the child needs
    // a separate dismissal signal for any open context menu.
    document.addEventListener('pointerdown', event => {
        if (event.target !== iframe) iframe.contentWindow?.postMessage({ type: 'zhengyan-desktop-sprite-dismiss-menu' }, player.origin);
    }, true);
    const mount = () => document.body.append(iframe);
    if (document.body) mount();
    else document.addEventListener('DOMContentLoaded', mount, { once: true });
})();

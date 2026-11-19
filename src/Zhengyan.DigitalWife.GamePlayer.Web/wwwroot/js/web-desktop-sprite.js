// Keep hit testing in the host browser's compositor so clicks remain trusted
// and reach the actual page element behind the iframe.
export const regionMessage = 'zhengyan-desktop-sprite-region';

export function createSpriteDrag(state) {
    let drag = null;
    const send = (phase, event) => window.parent.postMessage({type:'zhengyan-desktop-sprite-drag',phase,
        pointerId:event.pointerId,x:event.screenX,y:event.screenY},state.spriteHostOrigin);
    const end = event => {
        if (!drag || (event && event.pointerId !== drag.id)) return false;
        const previous=drag;drag=null;
        if(previous.host)send('end',event||{pointerId:previous.id,screenX:0,screenY:0});
        try{state.canvas.releasePointerCapture?.(previous.id);}catch{}
        return true;
    };
    return {
        get active(){return !!drag;},
        begin(event, captured) {
            const button=String(state.desktopSpriteDragButton||'').toLowerCase().replace(/[- ]/g,'_');
            const configured=['left','mouse_left','left_mouse'].includes(button)?0:
                ['right','mouse_right','right_mouse'].includes(button)?2:['middle','mouse_middle','middle_mouse'].includes(button)?1:-1;
            if(!state.desktopSpriteMode || captured || event.pointerType!=='mouse' || event.button!==configured)return false;
            const frame=state.canvas.parentElement;
            if(!state.spriteHostOrigin && (window.parent!==window || !frame))return false;
            end();
            const rect=frame?.getBoundingClientRect();
            drag={id:event.pointerId,button:event.button,host:!!state.spriteHostOrigin,x:event.screenX,y:event.screenY,
                left:rect?.left||0,top:rect?.top||0,frame};
            state.canvas.setPointerCapture?.(event.pointerId);
            if(drag.host)send('begin',event);
            else Object.assign(frame.style,{position:'fixed',width:`${rect.width}px`,height:`${rect.height}px`,left:`${rect.left}px`,top:`${rect.top}px`,margin:'0'});
            return true;
        },
        move(event) {
            if(!drag || event.pointerId!==drag.id)return false;
            const mask=drag.button===0?1:drag.button===2?2:4;
            if(!(event.buttons&mask)){end(event);return true;}
            if(drag.host)send('move',event);
            else {
                const rect=drag.frame.getBoundingClientRect();
                drag.frame.style.left=`${Math.max(0,Math.min(window.innerWidth-rect.width,drag.left+event.screenX-drag.x))}px`;
                drag.frame.style.top=`${Math.max(0,Math.min(window.innerHeight-rect.height,drag.top+event.screenY-drag.y))}px`;
            }
            return true;
        }, end
    };
}

export function alphaRegions(pixels, width, height, threshold = 8) {
    const rectangles = [], previous = new Map();
    for (let y = 0; y < height; y++) {
        const row = new Map();
        for (let x = 0; x < width;) {
            if (pixels[(y * width + x) * 4 + 3] < threshold) { x++; continue; }
            const left = x++;
            while (x < width && pixels[(y * width + x) * 4 + 3] >= threshold) x++;
            const key = `${left}:${x}`, old = previous.get(key);
            const rect = old || [left / width, y / height, (x - left) / width, 0];
            rect[3] += 1 / height;
            if (!old) rectangles.push(rect);
            row.set(key, rect);
        }
        previous.clear();
        for (const [key, rect] of row) previous.set(key, rect);
    }
    return rectangles;
}

export function createSpriteHitRegion(state) {
    const embedded = typeof window !== 'undefined' && window.parent !== window && window.parent?.postMessage;
    let enabled = false, sample, context, lastSample = -Infinity, lastMessage = '', pixels = [];
    const send = (rects, active = enabled) => {
        const message = { type: regionMessage, enabled: !!active, rects };
        const key = JSON.stringify(message);
        if (key === lastMessage) return;
        lastMessage = key;
        window.parent.postMessage(message, '*');
    };
    const domRegions = () => {
        const result = [];
        const width = Math.max(window.innerWidth, 1), height = Math.max(window.innerHeight, 1);
        for (const element of document.querySelectorAll('[role="menu"], .game-microphone-prompt, .game-fullscreen-prompt, .game-pointer-lock-prompt, .web-loading-screen, .embedded-error, .gui-text-editor')) {
            const rect = element.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) result.push([rect.left / width, rect.top / height, rect.width / width, rect.height / height]);
        }
        // Transparent GUI controls must remain operable even without painted pixels.
        const canvasRect = state.canvas.getBoundingClientRect();
        for (const control of state.overlayState?.controls || []) {
            if (control.visible === false) continue;
            result.push([(canvasRect.left + control.x * canvasRect.width / state.canvas.width) / width,
                (canvasRect.top + control.y * canvasRect.height / state.canvas.height) / height,
                control.width * canvasRect.width / state.canvas.width / width,
                control.height * canvasRect.height / state.canvas.height / height]);
        }
        return result;
    };
    return {
        configure(active) {
            enabled = !!active && !!embedded;
            if (embedded && !enabled) send([], false);
            lastSample = -Infinity;
        },
        publish(timestamp = performance.now(), force = false) {
            if (!enabled || !state.canvas || typeof document === 'undefined') return;
            if (state.pointers?.size || document.fullscreenElement || document.querySelector('.web-loading-screen')) { send([[0, 0, 1, 1]]); return; }
            if (timestamp - lastSample >= 100 || force) {
                lastSample = timestamp;
                sample ??= document.createElement('canvas');
                const rect = state.canvas.getBoundingClientRect(), longest = Math.max(rect.width, rect.height, 1);
                const width = Math.max(1, Math.round(128 * rect.width / longest)), height = Math.max(1, Math.round(128 * rect.height / longest));
                if (sample.width !== width || sample.height !== height) { sample.width = width; sample.height = height; }
                context ??= sample.getContext('2d', { willReadFrequently: true });
                if (!context) { send([[0, 0, 1, 1]]); return; }
                try {
                    context.clearRect(0, 0, width, height);
                    context.drawImage(state.canvas, 0, 0, width, height);
                    if (state.overlayCanvas) context.drawImage(state.overlayCanvas, 0, 0, width, height);
                    const scaleX = rect.width / Math.max(window.innerWidth, 1), scaleY = rect.height / Math.max(window.innerHeight, 1);
                    pixels = alphaRegions(context.getImageData(0, 0, width, height).data, width, height)
                        .map(([x, y, w, h]) => [rect.left / window.innerWidth + x * scaleX, rect.top / window.innerHeight + y * scaleY, w * scaleX, h * scaleY]);
                } catch { send([[0, 0, 1, 1]]); return; }
            }
            send([...pixels, ...domRegions()]);
        },
        stop() { if (embedded && enabled) send([]); enabled = false; sample = context = null; }
    };
}

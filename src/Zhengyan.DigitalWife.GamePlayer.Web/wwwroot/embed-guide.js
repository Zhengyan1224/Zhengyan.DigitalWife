const stylesheet = new URL('./css/embed-guide.css', import.meta.url).href;
const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
})[character]);

function webUrl(value) {
    let url;
    try { url = new URL(value); }
    catch { throw new Error('请填写完整的 HTTP / HTTPS 地址，例如 https://games.example.com/。'); }
    if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password)
        throw new Error('请填写完整的 HTTP / HTTPS 地址，不要在地址中包含用户名或密码。');
    url.hash = '';
    return url;
}

export function buildEmbedExamples({ mode = 'game', playerUrl, businessUrl, width, height }) {
    const player = webUrl(playerUrl), business = webUrl(businessUrl);
    width = Number(width); height = Number(height);
    if (![width, height].every(value => Number.isInteger(value) && value >= 1 && value <= 8192))
        throw new Error('宽度和高度应为 1 到 8192 之间的整数，单位为 CSS 像素。');
    const sprite = mode === 'sprite';
    const embedded = new URL(player);
    embedded.searchParams.set('embed', '1');
    const script = new URL('desktop-sprite.js', player);
    const html = sprite
        ? `<script defer\n  src="${escapeHtml(script.href)}"\n  data-src="${escapeHtml(player.href)}"\n  data-width="${width}"\n  data-height="${height}"\n  data-right="24"\n  data-bottom="0"\n  data-interactive="true">\n</script>`
        : `<div style="width:min(100%,${width}px);aspect-ratio:${width}/${height};">\n  <iframe\n    src="${escapeHtml(embedded.href)}"\n    title="我的游戏"\n    allow="autoplay; microphone; fullscreen"\n    allowfullscreen\n    style="display:block;width:100%;height:100%;border:0;background:transparent;">\n  </iframe>\n</div>`;
    return {
        html,
        playerHeaders: `# 播放器站点：在 HTML 响应中允许业务网站嵌入\nContent-Security-Policy: frame-ancestors 'self' ${business.origin};`,
        businessHeaders: `# 业务站点：将这些指令合并到已有 CSP\nContent-Security-Policy: ${sprite ? `script-src 'self' ${script.origin}; ` : ''}frame-src 'self' ${player.origin};\n\n# 业务站点如已设置 Permissions-Policy，需允许播放器使用这些功能\nPermissions-Policy: microphone=(self "${player.origin}"), autoplay=(self "${player.origin}"), fullscreen=(self "${player.origin}")`,
        resourceHeaders: `# 仅在模型、资源或 API 与播放器不同源时设置；配置在资源 / API 服务器\nAccess-Control-Allow-Origin: ${player.origin}\nVary: Origin\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\nAccess-Control-Allow-Headers: Content-Type, Authorization`,
        playerOrigin: player.origin,
        businessOrigin: business.origin
    };
}

if (typeof customElements !== 'undefined' && !customElements.get('web-embed-guide')) {
    customElements.define('web-embed-guide', class extends HTMLElement {
        connectedCallback() {
            if (this.shadowRoot) return;
            const sprite = this.getAttribute('mode') === 'sprite';
            const root = this.attachShadow({ mode: 'open' });
            root.innerHTML = `<link rel="stylesheet" href="${escapeHtml(stylesheet)}">
                <section aria-labelledby="guide-title">
                    <p class="eyebrow">开发者接入 · 支持同域与跨域网站</p>
                    <h2 id="guide-title">${sprite ? '把网页桌面精灵放进你的网站' : '把游戏画面嵌入你的网站'}</h2>
                    <p>${sprite ? '在业务页面引入下面的脚本，人物就会浮在页面上。该脚本会创建透明的播放器 iframe，并处理透明区域点击穿透与拖动。' : '在业务页面放入下面的 iframe，游戏、GUI、脚本与音频会一起运行。embed=1 会隐藏播放器标题和状态面板，让游戏填满 iframe。'}</p>
                    <ol class="steps">
                        <li>用 GameEditor 导出 Web 游戏，将游戏项目放入播放器的 <code>web-game/</code>。完整部署播放器发布目录，包含 <code>_framework/</code>、引擎、脚本和游戏资源。</li>
                        <li>将播放器部署到独立站点根目录，例如 <code>https://games.example.com/</code>，先直接打开确认游戏能运行。正式部署时播放器和业务页面都使用 HTTPS。</li>
                        <li>填写两个站点的实际地址，复制代码到业务页面。跨域时再按下方说明检查服务器响应头。</li>
                    </ol>
                    <form class="settings" novalidate>
                        <label class="wide">播放器入口地址<input name="player" type="url" required spellcheck="false" aria-describedby="address-help"></label>
                        <label class="wide">业务网站地址<input name="business" type="url" required spellcheck="false" placeholder="https://portal.example.com/"></label>
                        <label>宽度（CSS 像素）<input name="width" type="number" min="1" max="8192" required value="${sprite ? 500 : 960}"></label>
                        <label>高度（CSS 像素）<input name="height" type="number" min="1" max="8192" required value="${sprite ? 720 : 540}"></label>
                    </form>
                    <p class="hint" id="address-help">播放器地址用于生成嵌入代码；业务网站地址用于生成跨域白名单。示例会保留实际协议、域名和端口。</p>
                    <p class="validation" role="alert" hidden></p>
                    <div class="snippet"><div class="snippet-heading"><h3>复制到业务页面的 HTML</h3><button type="button" data-copy="html">复制嵌入代码</button></div><pre tabindex="0"><code data-code="html"></code></pre></div>
                    <p>${sprite ? '一页引入一次普通 script（保留 defer，不要改成 type="module"）。src 指向 desktop-sprite.js，data-src 指向播放器入口；不要把 desktop-sprite.html 预览页作为播放器地址。工程需开启桌面精灵模式；透明区域穿透沿用工程的点击穿透设置。' : '宽高和页面布局由业务网站控制；可修改示例比例或容器 CSS。iframe 加载的是播放器发布目录中的游戏，首页手动打开的本地 .dwgame 不会自动传给另一个 iframe。'}</p>
                    ${sprite ? '<p class="hint">data-right / data-bottom 设置右侧和底部边距；data-z-index 可设置层级；data-interactive="false" 会让整个人物浮层也穿透点击。使用脚本创建的 iframe 可保留人物、GUI、菜单与授权按钮的交互。</p>' : ''}
                    <details open>
                        <summary>跨域嵌入：两个站点分别需要允许什么？</summary>
                        <p>iframe 导航和普通外链脚本通常允许跨域，嵌入本身不要求把播放器资源的 CORS 全部打开。播放器内部仍从自己的站点加载游戏。参见 <a href="https://developer.mozilla.org/en-US/docs/Web/Security/Same-origin_policy#cross-origin_network_access" target="_blank" rel="noopener noreferrer">MDN 同源策略</a>。</p>
                        <div class="snippet"><div class="snippet-heading"><h3>播放器站点的 HTML 响应头</h3><button type="button" data-copy="playerHeaders">复制</button></div><pre tabindex="0"><code data-code="playerHeaders"></code></pre></div>
                        <p><code>frame-ancestors</code> 决定谁能嵌入播放器，必须设置为 HTTP 响应头，放在 meta 标签里无效。还需检查反向代理 / CDN 是否附加了 <code>X-Frame-Options: DENY</code> 或 <code>SAMEORIGIN</code>，它们可能阻止跨域嵌入。多层 iframe 时，所有祖先网站都须在白名单中。<a href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors" target="_blank" rel="noopener noreferrer">frame-ancestors 说明</a></p>
                        <div class="snippet"><div class="snippet-heading"><h3>业务站点的响应头</h3><button type="button" data-copy="businessHeaders">复制</button></div><pre tabindex="0"><code data-code="businessHeaders"></code></pre></div>
                        <p>业务网站的 <code>frame-src</code> 允许加载播放器 iframe${sprite ? '，script-src 还需允许外链桌面精灵脚本' : ''}。这些是需要合并的指令，<strong>请保留业务网站原有的其他 CSP 和 Permissions-Policy 配置</strong>；多个 CSP 会同时生效，追加一条宽松策略不能解除原有策略的限制。</p>
                        <p>麦克风、音频自动播放和全屏需要 iframe 的 <code>allow</code> 授权，上层页面及播放器自己的策略也不能禁止它们。桌面精灵脚本会自动添加 allow。麦克风仍需要用户同意，全屏和有声播放仍可能需要点击触发；不能用这些响应头跳过浏览器授权。<a href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe#allow" target="_blank" rel="noopener noreferrer">iframe 授权说明</a></p>
                    </details>
                    <details>
                        <summary>模型、游戏资源或 LLM API 也在其他域名：配置 CORS</summary>
                        <p>如果请求从游戏 iframe 发出，资源 / API 服务器应允许<strong>播放器的来源</strong>，不是只允许业务网站的来源。同源的 web-game 无需额外 CORS；单纯加 CORS 也不能解除 iframe 的 CSP / X-Frame-Options 限制。</p>
                        <div class="snippet"><div class="snippet-heading"><h3>资源 / API 服务器响应头示例</h3><button type="button" data-copy="resourceHeaders">复制</button></div><pre tabindex="0"><code data-code="resourceHeaders"></code></pre></div>
                        <p>服务器需正确响应 OPTIONS 预检，并按实际 API 缩小允许的方法和请求头。若请求携带 Cookie，还需配置 credentials 和明确的允许来源，不能使用 *；跨站 Cookie 也可能受浏览器限制。资源 / API 不便开放 CORS 时，可通过播放器站点的同源服务转发。<a href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/CORS" target="_blank" rel="noopener noreferrer">CORS 说明</a></p>
                    </details>
                    <details>
                        <summary>其他部署限制与排查</summary>
                        <ul>
                            <li>HTTPS 业务页应嵌入 HTTPS 播放器；HTTP 的 localhost / 127.0.0.1 仅适合本机开发，其他用户访问它会指向自己的电脑。</li>
                            <li>若业务框架额外设置 sandbox，至少需要允许脚本和同源身份（allow-scripts、allow-same-origin），再按游戏功能授权。空 sandbox 会阻止播放器正常加载。</li>
                            <li>若业务网站禁止内联样式，将 iframe 示例中的 style 移到业务网站自己的 CSS 类中，保留其原有安全策略。</li>
                            <li>若业务站点已开启 COEP=require-corp，播放器 HTML 需要兼容的 COEP 策略，外链脚本和跨域资源还需满足 CORP / CORS。仅允许 frame-src 不够；请按现有隔离方案配置。<a href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cross-Origin-Embedder-Policy" target="_blank" rel="noopener noreferrer">COEP 说明</a></li>
                            <li>跨域 iframe 的 DOM 不能由业务页直接读取；桌面精灵脚本已通过 postMessage 处理必要的宿主通信。播放器存档属于播放器来源，嵌入存储也可能受浏览器分区策略影响。</li>
                            <li>排查时查看浏览器 Console / Network：拒绝框架显示检查 CSP / X-Frame-Options；资源请求失败检查实际 URL 与 CORS；麦克风失败检查 HTTPS、allow、Permissions-Policy 和用户权限。</li>
                        </ul>
                    </details>
                    <p class="copy-status" aria-live="polite"></p>
                </section>`;
            const form = root.querySelector('form');
            form.elements.player.value = new URL('./', document.baseURI).href;
            form.elements.business.value = 'https://portal.example.com/';
            let examples;
            const update = () => {
                const validation = root.querySelector('.validation');
                try {
                    examples = buildEmbedExamples({ mode: sprite ? 'sprite' : 'game', playerUrl: form.elements.player.value,
                        businessUrl: form.elements.business.value, width: form.elements.width.value, height: form.elements.height.value });
                    for (const code of root.querySelectorAll('[data-code]')) code.textContent = examples[code.dataset.code];
                    validation.hidden = true;
                    for (const button of root.querySelectorAll('[data-copy]')) button.disabled = false;
                } catch (error) {
                    examples = null;
                    validation.textContent = error.message;
                    validation.hidden = false;
                    for (const button of root.querySelectorAll('[data-copy]')) button.disabled = true;
                }
            };
            form.addEventListener('input', update);
            form.addEventListener('submit', event => event.preventDefault());
            // Shadow DOM retargets input events to this element. Keep typing in the guide
            // out of the player's window keydown handler; keyup can still release held game keys.
            root.addEventListener('keydown', event => event.stopPropagation());
            root.addEventListener('click', async event => {
                const button = event.target.closest('[data-copy]');
                if (!button || !examples) return;
                const status = root.querySelector('.copy-status');
                try {
                    if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
                    await navigator.clipboard.writeText(examples[button.dataset.copy]);
                    status.textContent = '已复制，可粘贴到业务页面或服务器配置中。';
                } catch { status.textContent = '浏览器未允许复制，请选中上面的代码后手动复制。'; }
            });
            update();
        }
    });
}

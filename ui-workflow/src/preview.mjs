const escape = (value) => String(value).replace(/[&<>"']/g, (character) => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
})[character]);

export function renderPreview(bundle, files) {
  const assets = new Map(bundle.assets.map((entry) => [entry.id, entry]));
  const childrenByParent = Map.groupBy(bundle.nodes, (node) => node.parent);
  const background = (id) => {
    const asset = assets.get(id);
    const image = `url(data:image/png;base64,${files.get(asset.file).toString('base64')})`;
    if (!asset.border) return `background:${image} center/100% 100% no-repeat`;
    const { top, right, bottom, left } = asset.border;
    const edges = [top, right, bottom, left];
    return `border-image:${image} ${edges.join(' ')} fill / ${edges.map((edge) => `${edge}px`).join(' ')} stretch`;
  };
  const styles = [];
  function render(node, parent) {
    const { x, y, width, height } = node.rect;
    const right = parent.width - x - width;
    const bottom = parent.height - y - height;
    const placement = {
      topLeft: `left:${x}px;top:${y}px`,
      topRight: `right:${right}px;top:${y}px`,
      center: `left:calc(50% + ${x - parent.width / 2}px);top:calc(50% + ${y - parent.height / 2}px)`,
      bottomCenter: `left:calc(50% + ${x - parent.width / 2}px);bottom:${bottom}px`,
      stretch: `left:${x}px;top:${y}px;right:${right}px;bottom:${bottom}px`,
    }[node.anchor];
    const size = node.anchor === 'stretch' ? '' : `;width:${width}px;height:${height}px`;
    const style = [placement + size];
    let label = '';
    if (node.asset) style.push(background(node.asset));
    if (node.type === 'text') {
      style.push(`display:flex;align-items:center;justify-content:${{ left: 'flex-start', center: 'center', right: 'flex-end' }[node.align]}`);
      style.push(`color:${node.color};font-size:${node.fontSize}px;white-space:nowrap;overflow:hidden`);
      label = `<span>${escape(node.text)}</span>`;
    }
    if (node.type === 'button' && node.pressed) {
      styles.push(`#n_${node.id}:active{${background(node.pressed)} !important}`);
    }
    const tag = node.type === 'button' ? 'button' : 'div';
    const action = node.action ? ` type="button" aria-label="${node.action}" data-action="${node.action}"` : '';
    return `<${tag} id="n_${node.id}" class="node" style="${style.join(';')}"${action}>${label}${
      (childrenByParent.get(node.id) ?? []).map((child) => render(child, node.rect)).join('')}</${tag}>`;
  }

  const children = (childrenByParent.get('$root') ?? []).map((node) => render(node, bundle)).join('');
  return `<!doctype html>
<html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>${bundle.id}</title>
<style>
body{margin:0;background:#12151b;color:#e6e9ee;font-family:system-ui,sans-serif}
header{padding:12px 16px;display:flex;align-items:center;gap:12px;flex-wrap:wrap}
input{width:5em} output{margin-left:auto} main{margin:0 16px 16px;overflow:auto}
#screen{width:${bundle.width}px;height:${bundle.height}px;position:relative;overflow:hidden;background:#19202b;isolation:isolate}
.node{position:absolute;box-sizing:border-box}
button.node{padding:0;border:0;cursor:pointer;background:transparent;color:inherit;font:inherit}
${styles.join('\n')}
</style>
<header><strong>${bundle.id}</strong>
<label>宽 <input id="width" type="number" min="1" max="4096" value="${bundle.width}"></label>
<label>高 <input id="height" type="number" min="1" max="4096" value="${bundle.height}"></label>
<output id="action" aria-live="polite"></output></header>
<main><section id="screen" aria-label="界面预览">${children}</section></main>
<script>
const screenElement = document.getElementById('screen');
for (const axis of ['width', 'height']) {
  const input = document.getElementById(axis);
  input.addEventListener('input', () => {
    if (input.value && input.validity.valid) screenElement.style[axis] = input.value + 'px';
  });
}
screenElement.addEventListener('click', (event) => {
  const button = event.target.closest('[data-action]');
  if (button) document.getElementById('action').textContent = button.dataset.action;
});
</script></html>`;
}

from pathlib import Path
import re

base = Path(__file__).parent
source = (base.parent / 'api-receipt.html').read_text(encoding='utf-8-sig')
source = re.sub(r'<link[^>]+>', '', source)
source = source.replace('(() => {', '''(async () => {
 await Promise.all([document.fonts.load('13px "DotGothic16"'),document.fonts.load('42px "VT323"')]);
 await document.fonts.ready;''', 1)
source = source.replace('const resetAt=Date.now()+(3*86400+4*3600+22*60)*1000;', 'let resetAt=0;')
start = source.index(' function regularMarkup(')
end = source.index(' let audioContext;', start)
source = source[:start] + r'''
 function brandStamp(){return `<div class="store-brand" role="img" aria-label="Codex"><span class="store-symbol" aria-hidden="true">CODEX_SYMBOL_ASSET</span><span class="store-wordmark" aria-hidden="true">CODEX_WORDMARK_ASSET</span></div>`;}
 function quotaMarkup(w){return `<div class="pair"><span>${w.label}剩余</span><span class="metric">${w.remaining}%</span></div>${quotaBar(w.remaining)}`;}
 function fixedDuration(target,printedAt){const m=Math.max(0,Math.ceil((target-printedAt)/60000));return `${Math.floor(m/1440)}天 ${Math.floor(m%1440/60)}小时 ${m%60}分`;}
 function regularMarkup(kind,r){
  const head=`${brandStamp()}<div class="brand">账单</div><div class="subtitle">PLAN USAGE</div><div class="pair"><span>ChatGPT 订阅</span><span>#${r.id}</span></div><div class="rule"></div>`;
  const use=`<div class="pair"><span class="muted">本次费用</span><span>使用订阅额度</span></div><div class="hero-label">${r.snapshot?'最近一次 Token 用量':r.partial?'本次 Token 用量 · 启动后':'本次 Token 用量'}</div><div class="hero-number">${r.hasUsage===false?'—':tokens(r.input+r.output)}<small>TOKENS</small></div><div class="pair detail"><span>输入 ${r.hasUsage===false?'—':tokens(r.input)}</span><span>输出 ${r.hasUsage===false?'—':tokens(r.output)}</span></div><div class="detail">缓存 ${r.hasUsage===false?'—':tokens(r.cached)} · 已含在输入中</div>`;
  const quotas=(r.windows||[]).map(quotaMarkup).join('<div class="rule"></div>')||'<div class="detail">等待账户额度数据</div>';
  const w=r.windows?.[0],extra=(r.windows||[]).slice(1).map(v=>`<div class="pair"><span>${v.label}重置</span><time>${v.resetAt?timestamp(v.resetAt):'未知'}</time></div>`).join('');
  const stale=r.quotaAt&&r.printedAt-r.quotaAt>120000?`<div class="detail">额度采样 ${timestamp(r.quotaAt)}</div>`:'';
  return `${head}${use}<div class="rule"></div>${quotas}<div class="rule"></div><div class="pair"><span>打印时距重置</span><span>${w?.resetAt?fixedDuration(w.resetAt,r.printedAt):'未知'}</span></div><div class="times"><div class="pair"><span>打印时间</span><time>${timestamp(r.printedAt)}</time></div><div class="pair"><span>重置时间</span><time>${w?.resetAt?timestamp(w.resetAt):'未知'}</time></div>${extra}</div>${stale}<div class="foot">北京时间 · 本票为打印时快照</div>`;
 }
 function warningMarkup(kind,r){const w=r.warning,red=w.remaining<=0;return `${brandStamp()}<div class="alert-title">警告</div><div class="alert-label">${w.label}剩余额度</div><div class="alert-value">${w.remaining}%</div>${quotaBar(w.remaining)}${red?`<div class="rule"></div><div class="times"><div class="pair"><span>重置时间</span><time>${w.resetAt?timestamp(w.resetAt):'未知'}</time></div>${w.resetAt?`<div class="pair"><span>打印时距重置</span><span>${fixedDuration(w.resetAt,r.printedAt)}</span></div>`:''}</div>`:''}`;}
 function resetMarkup(r){const w=r.reset.window;return `${brandStamp()}<div class="alert-title">额度已重置</div><div class="alert-label">${w.label}剩余</div><div class="alert-value">${w.remaining}%</div>${quotaBar(w.remaining)}<div class="rule"></div><div class="times"><div class="pair"><span>重置时间</span><time>${timestamp(r.reset.resetAt)}</time></div><div class="pair"><span>下次重置</span><time>${timestamp(w.resetAt)}</time></div></div>`;}
 function makeSpecialReceipt(tone,current,printedAt,id){
  const base={id,demo:true,printedAt,warnings:[]};
  if(tone==='cyan')return {...base,resetOnly:true,reset:{resetAt:printedAt,window:{...current,remaining:100,resetAt:printedAt+(current.minutes||10080)*60000}}};
  if(tone==='yellow'||tone==='red')return {...base,warningOnly:true,warning:{...current,remaining:tone==='yellow'?10:0}};
  return null;
 }
 function makeShowcaseRecords(current,printedAt){
  const quotas=[72,48,24,9.8,0],inputs=[12240,28400,18600,36200,9400],outputs=[860,1750,1220,2140,680];
  const records=quotas.map((remaining,i)=>{
   const window={...current,remaining};
   return {id:String(i+1).padStart(6,'0'),demo:true,showcase:true,snapshot:false,hasUsage:true,partial:false,
    input:inputs[i],cached:Math.floor(inputs[i]*.6/100)*100,output:outputs[i],printedAt,windows:[window],quotaAt:printedAt,warnings:i>=3?[window]:[]};
  });
  records.push({...makeSpecialReceipt('cyan',current,printedAt,'000006'),showcase:true});
  return records;
 }
''' + source[end:]
source = source.replace('CODEX_SYMBOL_ASSET', (base/'assets/codex-symbol-monochrome.svg').read_text(encoding='utf-8').strip())
source = source.replace('CODEX_WORDMARK_ASSET', (base/'assets/codex-wordmark-monochrome.svg').read_text(encoding='utf-8').strip())
# Keep the original printing and accordion mechanics, replace only the demo input.
source = source.replace("[...root.querySelectorAll('section')].forEach((section,kind)=>{", "[...root.querySelectorAll('section')].forEach((section,kind)=>{if(!kind){section.remove();return;}section.hidden=false;")
source = source.replace("const preview={id:'000128',input:20400,cached:8000,output:1860,cost:421,after:kind?68:199579,printedAt:Date.now()};", "const preview={id:'LIVE',snapshot:true,hasUsage:false,windows:[],printedAt:Date.now()};")
source = source.replace('<div class="controls">', '<div class="controls"><button class="hide" type="button">收起</button><button class="quit" type="button">退出</button>')
source = source.replace("const isWarning=type==='warning',label=isWarning?'警告':'消费';", "const isWarning=type==='warning',isReset=type==='reset',label=isReset?'额度重置':isWarning?'警告':record.snapshot?'额度快照':'用量';")
source = source.replace("const tone=isWarning?level(kind,record.after):'normal';", "const tone=isReset?'cyan':isWarning?level(kind,record.warning.remaining):'normal';")
source = source.replace("value<=(kind?20:50000)", "value<=(kind?10:50000)")
source = source.replace('const html=isWarning?warningMarkup(kind,record):regularMarkup(kind,record);', 'const html=isReset?resetMarkup(record):isWarning?warningMarkup(kind,record):regularMarkup(kind,record);')
source = source.replace("const mode=queue.shift();update();const record=makeTransaction(mode);await printDocument(record);", "const record=queue.shift();update();await printDocument(record);")
source = source.replace("const record=queue.shift();update();await printDocument(record);", "const record=queue.shift();update();await printDocument(record,record.resetOnly?'reset':record.warningOnly?'warning':'usage');")
source = source.replace('${w.label}剩余额度</div>', '${w.label}剩余</div>')
source = source.replace("if(record.warn){", "for(const warning of record.warnings||[]){record.warning=warning;")
source = source.replace("history.push(item);finished=true;update();", "history.push(item);finished=true;update();const face=node.querySelector('.fold-slice>.paper')||node;const style=getComputedStyle(face);window.chrome.webview.postMessage({type:'printed',id:record.id,receipt:type,typography:{font:style.fontFamily,fontSize:style.fontSize,padding:style.padding,width:face.getBoundingClientRect().width,dotGothic16:document.fonts.check('13px DotGothic16'),vt323:document.fonts.check('42px VT323'),heroFont:face.querySelector('.hero-number')?getComputedStyle(face.querySelector('.hero-number')).font:null}});")
source = source.replace("run.push(item);activeItem=null;", "run.push(item);activeItem=null;paperPrinted(item);")
source = source.replace("type:'printed',id:record.id,receipt:type,", "type:'printed',id:record.id,receipt:type,demo:!!record.demo,")
source = source.replace("receipt:type,demo:!!record.demo,", "receipt:type,demo:!!record.demo,showcase:!!record.showcase,tone,remaining:isReset?record.reset.window.remaining:isWarning?record.warning.remaining:record.windows?.[0]?.remaining,soundEnabled:sound.checked,audioState:audioContext?.state||'idle',")
source = source.replace("item.node.setAttribute('aria-hidden','true');", "item.node.setAttribute('aria-label','折叠小票，悬停展开');item.node.classList.add('creased');")
source = source.replace("const item={node,html,tone,id:", "const item={node,html,tone,id:")
# Pointer expansion must settle before a new continuous-feed animation starts.
source = source.replace("const sample=strip.querySelector('.sample');", "settleHover();const sample=strip.querySelector('.sample');")
start = source.index('  function makeTransaction(')
end = source.index('  function printFrames(',start)
source = source[:start] + source[end:]
start = source.index('  function buildAccordion(')
end = source.index('  function printFrames(',start)
source = source[:start] + (base/'paper-folds.js').read_text(encoding='utf-8') + '\n' + source[end:]
start = source.index('  function enqueue(')
end = source.index("  tear.addEventListener",start)
source = source[:start] + r'''
  q('.single').remove();q('.batch').remove();scenario.closest('label').remove();
  q('.example').replaceChildren(counter);status.textContent='正在连接 Codex…';strip.replaceChildren();
  q('.hide').onclick=()=>window.chrome.webview.postMessage({type:'hide'});
  q('.quit').onclick=()=>window.chrome.webview.postMessage({type:'quit'});
  let lastRecord=null,demoSerial=0;
  sound.checked=localStorage.getItem('receipt.sound')!=='false';
  function setPrintSound(enabled){sound.checked=!!enabled;localStorage.setItem('receipt.sound',String(sound.checked));window.chrome.webview.postMessage({type:'soundState',enabled:sound.checked});}
  function printSpecial(tone){
   const current=lastRecord?.windows?.[0]||{label:'额度',resetAt:0,minutes:10080};
   const record=makeSpecialReceipt(tone,current,Date.now(),({yellow:'Y',red:'R',cyan:'C'}[tone]||'S')+String(++demoSerial).padStart(3,'0'));
   if(record){queue.push(record);void drain();}
  }
  window.chrome.webview.addEventListener('message',e=>{
   if(e.data.type==='showcase'){
    setPrintSound(true);
    const printedAt=Date.now(),current=e.data.window||{label:'周额度',minutes:10080,resetAt:printedAt+7*86400000};
    const records=makeShowcaseRecords(current,printedAt);lastRecord=records[0];queue.push(...records);void drain();
   }
   if(e.data.type==='receipt'){if(!e.data.record.resetOnly&&!e.data.record.warningOnly)lastRecord=e.data.record;queue.push(e.data.record);void drain();}
   if(e.data.type==='demoBatch'&&lastRecord){
    const count=Math.max(1,Math.min(10,Number(e.data.count)||10));
    for(let i=0;i<count;i++)queue.push({...lastRecord,id:'D'+String(++demoSerial).padStart(3,'0'),demo:true,warnings:[],printedAt:Date.now()});
    void drain();
   }
   if(e.data.type==='printSpecial')printSpecial(e.data.tone);
   if(e.data.type==='demoWarnings'){printSpecial('yellow');printSpecial('red');}
   if(e.data.type==='status'){q('.machine-label span').textContent=e.data.text;}
  });
  window.chrome.webview.postMessage({type:'ready'});
  window.chrome.webview.postMessage({type:'soundState',enabled:sound.checked});
''' + source[end:]
source = source.replace("\n });\n const countdownTimer", '\n'+(base/'paper-interactions.js').read_text(encoding='utf-8')+"\n });\n const countdownTimer")
source = source.replace("\n });\n const countdownTimer", '\n'+(base/'paper-marker.js').read_text(encoding='utf-8')+"\n });\n const countdownTimer")
source = source.replace("\n });\n const countdownTimer", '\n'+(base/'paper-tear.js').read_text(encoding='utf-8')+"\n });\n const countdownTimer")
source = source.replace("if(e.data.action==='sound')sound.checked=e.data.enabled;", "if(e.data.action==='sound')setPrintSound(e.data.enabled);")
# All printed times are immutable snapshots; no timer mutates existing receipt text.
source = re.sub(r' function refreshCountdowns\(\).*?\n', '', source)
source = re.sub(r' const countdownTimer=setInterval\(.*?\n', '', source)
source = source.replace('refreshCountdowns();', '')
# Keep the preview's font stacks and all receipt typography unchanged.
top_edge = [f'{i}% {"2px" if i % 2 == 0 else "0px"}' for i in range(101)]
bottom_edge = [f'{i}% {"calc(100% - 2px)" if i % 2 == 0 else "100%"}' for i in range(100, -1, -1)]
paper_edge = 'polygon(' + ','.join(top_edge + bottom_edge) + ')'
style = '''<style>
@font-face{font-family:'DotGothic16';src:url('fonts/DotGothic16-Regular.ttf') format('truetype');font-weight:400;font-style:normal;font-display:block}
@font-face{font-family:'VT323';src:url('fonts/VT323-Regular.ttf') format('truetype');font-weight:400;font-style:normal;font-display:block}
:root{color-scheme:light}html,body{margin:0;background:transparent;overflow:hidden}
#api-receipt-preview .scene{height:100vh;padding:0}
#api-receipt-preview .controls,#api-receipt-preview .example,#api-receipt-preview .status,#api-receipt-preview .machine,#api-receipt-preview .readback{display:none!important}
#api-receipt-preview button{padding:5px 7px}
#api-receipt-preview .assembly{width:100%;right:0;bottom:0}
#api-receipt-preview .window{height:100vh;bottom:0}
#api-receipt-preview .strip{left:auto;right:3px;width:290px}
#api-receipt-preview .brand,#api-receipt-preview .subtitle,#api-receipt-preview .hero-label,#api-receipt-preview .alert-title,#api-receipt-preview .alert-label{white-space:nowrap}
#api-receipt-preview .paper:not(.accordion){clip-path:PAPER_EDGE}
#api-receipt-preview .paper:not(.accordion){box-shadow:inset 1px 0 3px #0000000a,inset -1px 0 3px #0000000a,inset 0 -2px 4px #00000012}
#api-receipt-preview .paper.red{--paper:#df391e;--ink:#281c10;--muted-ink:#4a2717;--paper-line:#9e301c}
#api-receipt-preview .paper.cyan{--paper:#a6e4df;--ink:#173d39;--muted-ink:#38645e;--paper-line:#69aaa2}
#api-receipt-preview .paper.cyan .alert-title{font-size:24px}
#api-receipt-preview .store-brand{display:flex;align-items:center;justify-content:center;gap:11px;width:210px;height:62px;max-width:100%;margin:2px auto 18px;background:var(--ink);color:var(--paper);padding:13px 16px}
#api-receipt-preview .store-brand span{display:flex;align-items:center;flex:none}
#api-receipt-preview .store-brand{position:relative;clip-path:polygon(.4% 1%,12% .6%,12.2% 1.4%,31% .3%,48% .9%,63% .1%,78% .7%,99.6% .4%,100% 28%,99.6% 28.8%,99.9% 65%,99.5% 99.3%,81% 99.7%,80.6% 99%,57% 99.8%,35% 99.2%,34.7% 100%,.2% 99.4%,.5% 71%,0 70.5%,.3% 38%)}
#api-receipt-preview .store-brand::after,#api-receipt-preview .store-brand span::after{content:'';position:absolute;inset:0;pointer-events:none;mask-image:url('assets/ink-grain.svg');mask-size:210px 62px;background:var(--paper);opacity:.24}
#api-receipt-preview .store-brand span{position:relative}
#api-receipt-preview .store-brand span::after{background:var(--ink);opacity:.3;mask-position:-26px -15px}
#api-receipt-preview .store-symbol svg{display:block;width:32px;height:32px}
#api-receipt-preview .store-wordmark svg{display:block;width:124px;height:33px}
#api-receipt-preview .status{left:14px;font-size:10px}
#api-receipt-preview .readback{inset:85px 10px 64px}
#api-receipt-preview .machine-label{font-size:10px;letter-spacing:0}
.cursor-interaction{cursor:pointer}
#api-receipt-preview .tear-bundle{position:relative;width:100%;transform-origin:100% 100%;will-change:transform;pointer-events:none}
#api-receipt-preview .paper.creased .fold-panel .paper{color:color-mix(in srgb,var(--ink) 91%,var(--paper))}
#api-receipt-preview .paper.creased .fold-panel .paper .detail,#api-receipt-preview .paper.creased .fold-panel .paper .foot{color:color-mix(in srgb,var(--muted-ink) 94%,var(--paper))}
#api-receipt-preview .paper.creased .crease-line{height:2px;background:linear-gradient(to bottom,color-mix(in srgb,var(--ink) 22%,var(--paper)),color-mix(in srgb,var(--paper) 92%,var(--ink)));opacity:.8}
#api-receipt-preview .paper.creased .fold-panel::before{content:'';position:absolute;z-index:2;left:0;right:0;bottom:0;height:3px;background:var(--paper);opacity:.25;pointer-events:none}
#api-receipt-preview .paper.accordion{isolation:isolate}
#api-receipt-preview .paper.accordion .fold-panel{opacity:var(--fold-face,1)}
#api-receipt-preview .fold-slice>.paper{clip-path:none!important;box-shadow:none!important}
#api-receipt-preview .fold-back{position:absolute;inset:0;width:100%;height:100%;opacity:var(--fold-back,0);overflow:visible;pointer-events:none}
#api-receipt-preview .back-upper{fill:var(--paper)}
#api-receipt-preview .back-middle{fill:color-mix(in srgb,var(--paper) 96%,#706550)}
#api-receipt-preview .back-lower{fill:color-mix(in srgb,var(--paper) 92%,#706550)}
#api-receipt-preview .back-seam{fill:none;stroke:color-mix(in srgb,var(--paper) 82%,#706550);stroke-width:.5}
#api-receipt-preview .reading-face{position:absolute;inset:0;display:var(--reading-display,none)}
#api-receipt-preview .reading-face>.paper{clip-path:none;box-shadow:none}
#api-receipt-preview .curl-flat{position:absolute;left:0;right:0;top:0;overflow:hidden;background:var(--paper)}
#api-receipt-preview .curl-flat>.paper{clip-path:none;box-shadow:none}
#api-receipt-preview .paper.creased .curl-band::before{display:none}
#api-receipt-preview .paper.creased .curl-band .paper{color:var(--ink)}
#api-receipt-preview .curl-band::after{opacity:1;background:linear-gradient(to bottom,rgb(32 27 16 / var(--curl-shade-start,0)),rgb(32 27 16 / var(--curl-shade-end,0)))}
#api-receipt-preview .marker-censor{color:transparent!important;text-shadow:none!important;box-decoration-break:clone;-webkit-box-decoration-break:clone;background-image:var(--marker-stroke);background-size:100% 100%;background-repeat:no-repeat}
#api-receipt-preview .marker-censor::selection{color:transparent;background:#252522}
</style>'''
style = style.replace('PAPER_EDGE', paper_edge)
# Overrides must precede the script, so dimensions are final before printing.
source = source.replace('<script>', style+'<script>',1)
(base / 'index.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'+source+'</html>',encoding='utf-8')
assert 'const countdownTimer' not in source and 'data-reset-at=' not in source
assert '演示' not in source and 'completionMarkup' not in source
print('Built desktop UI: static print times, cyan quota-reset receipt, no demo labels.')

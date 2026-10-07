  function foldIdentity(item){
   if(item.foldIdentity)return item.foldIdentity;
   let hash=2166136261;for(const ch of item.id)hash=Math.imul(hash^ch.charCodeAt(0),16777619);
   hash=(hash^(hash>>>16))>>>0;
   return item.foldIdentity={height:8+(hash%61)/10,x:((hash>>>7)%43-21)/10,curve:((hash>>>13)%11-5)/10};
  }
  function compactSize(item){return foldIdentity(item).height;}
  function buildAccordion(item){
   if(item.panels)return;
   item.flatHeight=item.node.getBoundingClientRect().height;item.projectedHeight=item.flatHeight;
   const id=foldIdentity(item),bow=id.curve*2;
   item.node.style.height=`${item.flatHeight}px`;item.node.classList.add('accordion','creased');
   item.node.setAttribute('aria-label',`折叠小票 ${item.id}，悬停展开`);
   item.node.innerHTML=`<div class="curl-flat"><div class="paper ${item.tone}">${item.html}</div></div>`+Array.from({length:10},()=>`<div class="fold-panel curl-band"><div class="fold-slice" style="height:${item.flatHeight}px"><div class="paper ${item.tone}">${item.html}</div></div></div>`).join('')+
    `<svg class="fold-back" viewBox="0 0 290 24" preserveAspectRatio="none" aria-hidden="true">
     <path class="back-lower" d="M3 5 Q75 ${2+bow} 147 5 T287 4 Q292 13 287 22 Q215 ${24-bow} 145 21 T3 22 Q-1 14 3 5Z"/>
     <path class="back-upper" d="M3 4 Q75 ${1+bow} 147 4 T287 3 Q290 8 287 16 Q216 ${19-bow} 145 16 T3 17 Q0 10 3 4Z"/>
     <path class="back-seam" d="M4 18 Q74 ${15+bow} 145 17 T286 16"/>
    </svg><div class="reading-face"><div class="paper ${item.tone}">${item.html}</div></div>`;
   item.panels=[...item.node.querySelectorAll('.fold-panel')];item.flat=item.node.querySelector('.curl-flat');item.slices=[...item.node.querySelectorAll('.fold-slice')];item.back=item.node.querySelector('.fold-back');
   commitFold(item,item.flatHeight);
  }
  function foldPose(item,projected){
   const ratio=Math.min(1,Math.max(0,projected/item.flatHeight));
   // Opaque faces until the fold is almost closed; never blend text with a stretched paper back.
   const face=projected>Math.max(24,compactSize(item)*2.2)?1:0;
   return {ratio,angle:Math.acos(ratio)*180/Math.PI,step:projected/4,face};
  }
  function commitFold(item,height){
   const pose=foldPose(item,height),id=foldIdentity(item);item.projectedHeight=height;item.node.style.height=`${height}px`;
   item.node.style.transform=`translateX(${id.x*(1-pose.ratio)}px)`;
   item.node.style.setProperty('--fold-face',String(pose.ratio>.999?0:pose.face));
   item.node.style.setProperty('--reading-display',pose.ratio>.999?'block':'none');
   item.node.style.setProperty('--fold-back',String(1-pose.face));
   item.node.dataset.compact=String(pose.ratio<.92);
   if(!item.previewCaptured&&pose.ratio>.45&&pose.ratio<.65){item.previewCaptured=true;window.chrome.webview.postMessage({type:'curlPreview'});}
   const bend=Math.max(0,item.flatHeight-height),angle=Math.min(1.48,bend/80*1.48);
   const curl=Math.min(58,height*.45,bend*.55),flat=Math.max(0,height-curl),radius=angle>.001?curl/Math.sin(angle):0;
   item.flat.style.height=`${flat}px`;item.flat.style.display=pose.ratio>.999||!pose.face?'none':'block';
   item.panels.forEach((panel,i)=>{
    const a=angle*i/item.panels.length,b=angle*(i+1)/item.panels.length,source=radius*(b-a),y=radius*Math.sin(a),projected=radius*(Math.sin(b)-Math.sin(a));
    panel.style.top=`${flat+y}px`;panel.style.height=`${source+.06}px`;panel.style.transform=`scaleY(${source>.001?projected/source:1})`;
    panel.style.setProperty('--curl-shade-start',String(.18*Math.pow(Math.sin(a),2)));
    panel.style.setProperty('--curl-shade-end',String(.18*Math.pow(Math.sin(b),2)));
    item.slices[i].style.top=`${-(flat+radius*a)}px`;
   });
  }
  function preparePushFolds(distance){
   const occupied=run.reduce((sum,item)=>sum+(item.projectedHeight??item.node.getBoundingClientRect().height),0);
   const headroom=Math.max(0,innerHeight-2-occupied);
   const needed=Math.max(0,distance-headroom);
   if(needed<=0)return [];
   const plans=[];let absorbed=0;
   for(let i=run.length-1;i>=0&&absorbed<needed;i--){
    const item=run[i],start=item.projectedHeight??item.node.getBoundingClientRect().height,capacity=Math.max(0,start-compactSize(item));
    if(capacity<.01)continue;
    buildAccordion(item);
    const amount=Math.min(capacity,needed-absorbed);plans.push({item,start,end:start-amount,begin:headroom+absorbed,amount});absorbed+=amount;
   }
   return plans;
  }
  function startPushFolds(plans,motion,distance,duration){
   return plans.map(plan=>{
    // Compression cannot exceed the amount of new paper actually fed into the stack.
    const frames=motion.stops.map(stop=>({offset:stop.offset,easing:'steps(1,end)',height:`${plan.start-Math.min(plan.amount,Math.max(0,distance*stop.progress-plan.begin))}px`}));
    const animation=plan.item.node.animate(frames,{duration,easing:'linear',fill:'forwards'});
    plan.item.feedAnimation=animation;
    return animation;
   });
  }

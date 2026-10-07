  // Native window clipping follows paper geometry; the rest of the desktop is untouched.
  let pointer={x:-1,y:-1},hovered=null,menuOpen=false;
  let tearGesture=null,tearing=false;
  const foldTweens=new Map();
  let hoverAnchor=null;
  function paperRects(){
   return [...run,...(activeItem?[activeItem]:[])].filter(item=>item.node.isConnected).map(item=>{
    const r=item.node.getBoundingClientRect();
    const folded=!!item.panels&&item.projectedHeight<item.flatHeight*.92;
    return {item,x:Math.max(0,r.left),y:Math.max(0,r.top),width:Math.min(innerWidth,r.right)-Math.max(0,r.left),height:Math.min(innerHeight,r.bottom)-Math.max(0,r.top),folded,topEdge:!folded&&r.top>=0,bottomEdge:!folded&&r.bottom<=innerHeight};
   }).filter(r=>r.width>0&&r.height>0);
  }
  function tweenFold(item,target,delay=0){
   if(!item.node.isConnected)return;
   buildAccordion(item);
   const from=item.projectedHeight;
   if(Math.abs(from-target)<.1)return;
   foldTweens.set(item,{from,target,start:performance.now()+delay,duration:target>from?520:360});
  }
  function settleHover(){
   for(const [item,tween] of foldTweens)commitFold(item,tween.target);
   foldTweens.clear();hovered=null;hoverAnchor=null;
  }
  function foldEverything(){
   hovered=null;hoverAnchor=null;
   // Printing owns its own accordion animation until the feed completes.
   if(!busy)[...run].reverse().forEach((item,i)=>tweenFold(item,compactSize(item),Math.min(i*55,550)));
  }
  function paperPrinted(item){
   for(const older of run)if(older!==item)older.holdUntil=0;
   item.holdUntil=performance.now()+5000;
   // A restrained vertical settling ripple, never a sideways wobble.
   run.slice(-4,-1).reverse().forEach((old,i)=>{if(old.panels)old.node.animate([{translate:'0 -1px'},{translate:'0 0px'}],{duration:260,delay:i*35,easing:'ease-out'});});
  }
  function feedLocked(item){return item.feedAnimation?.playState==='running';}
  function collapse(item,force=false,targetOverride){
   if(!item||feedLocked(item)||(!force&&performance.now()<(item.holdUntil??0)))return;
   const target=targetOverride??compactSize(item),current=item.projectedHeight??item.node.getBoundingClientRect().height;
   if(current<=target+.1)return;
   if(foldTweens.get(item)?.target===target)return;
   tweenFold(item,target);
  }
  function makeReadingRoom(selected){
   const others=run.filter(item=>item!==selected),space=Math.max(1,innerHeight-4-(selected.flatHeight??selected.node.getBoundingClientRect().height));
   const thickness=others.reduce((sum,item)=>sum+compactSize(item),0),scale=Math.min(1,space/Math.max(1,thickness));
   for(const item of others)collapse(item,true,Math.max(.5,compactSize(item)*scale));
  }
  function atBoundary(item){
   const r=item.node.getBoundingClientRect();
   // The bottom is the intentional paper outlet; the top and sides are capacity limits.
   return r.top<=2||r.left<-.5||r.right>innerWidth+.5||r.bottom>innerHeight+1;
  }
  function hitPaper(){return paperRects().find(r=>pointer.x>=r.x&&pointer.x<=r.x+r.width&&pointer.y>=r.y&&pointer.y<=r.y+r.height)?.item;}
  function checkPointer(){
   let hit=hitPaper();
   if(hovered&&hoverAnchor&&pointer.x>=0&&pointer.y>=0&&Math.hypot(pointer.x-hoverAnchor.x,pointer.y-hoverAnchor.y)<5)hit=hovered;
   if(menuOpen||tearGesture||tearing)return;
   if(!busy&&hit&&hit!==activeItem&&hit!==hovered){
    collapse(hovered);
    hovered=hit;
    hoverAnchor={...pointer};
    makeReadingRoom(hit);
    if(hit.panels)tweenFold(hit,hit.flatHeight);
   }
   if(!hit&&!menuOpen&&hovered){
    collapse(hovered);
    hovered=null;hoverAnchor=null;
   }
   for(const item of run){
    if(!busy&&atBoundary(item)){
     collapse(item,true);
     // Do not repeatedly reopen a paper that cannot fit until the cursor moves again.
    }else if(item!==hit)collapse(item);
   }
  }
  strip.addEventListener('pointermove',e=>{pointer={x:e.clientX,y:e.clientY};checkPointer();});
  strip.addEventListener('pointerleave',()=>{pointer={x:-1,y:-1};checkPointer();});
  strip.addEventListener('contextmenu',e=>{
   if(!e.target.closest('.paper'))return;
   e.preventDefault();menuOpen=true;
   window.chrome.webview.postMessage({type:'contextMenu',x:e.clientX,y:e.clientY,viewportWidth:innerWidth,viewportHeight:innerHeight,busy,sound:sound.checked,hasPapers:run.length>0});
  });
  window.chrome.webview.addEventListener('message',e=>{
   if(e.data.type==='pointer'){pointer={x:e.data.x,y:e.data.y};checkPointer();}
   if(e.data.type==='menuClosed'){menuOpen=false;checkPointer();}
   if(e.data.type==='action'){
    if(e.data.action==='sound')sound.checked=e.data.enabled;
    if(e.data.action==='tear')tear.click();
    if(e.data.action==='fold')foldEverything();
    if(e.data.action==='show'&&!busy&&run.length){const item=run.at(-1);item.holdUntil=performance.now()+5000;makeReadingRoom(item);buildAccordion(item);tweenFold(item,item.flatHeight);}
   }
  });
  let lastRegion='',lastFrame=0;
  function surfaceFrame(now){
   for(const [item,t] of foldTweens){
    const p=Math.min(1,Math.max(0,(now-t.start)/t.duration)),smooth=p*p*(3-2*p);
    let height=p>=1?t.target:t.from+(t.target-t.from)*smooth;
    if(item===hovered&&t.target>t.from){
     const occupied=run.filter(other=>other!==item).reduce((sum,other)=>sum+(other.projectedHeight??other.node.getBoundingClientRect().height),0);
     height=Math.min(height,Math.max(compactSize(item),innerHeight-4-occupied));
    }
    commitFold(item,height);
    if(p>=1&&Math.abs(height-t.target)<.1)foldTweens.delete(item);
   }
   for(const item of run){if(item.feedAnimation&&['running','finished'].includes(item.feedAnimation.playState))commitFold(item,item.node.getBoundingClientRect().height);}
   if(now-lastFrame>32){
    lastFrame=now;checkPointer();
    const rects=paperRects().map(({x,y,width,height,topEdge,bottomEdge,folded})=>({x:Math.floor(x),y:Math.floor(y),width:Math.ceil(width),height:Math.ceil(height),topEdge,bottomEdge,folded}));
    const region=JSON.stringify(rects);
    if(region!==lastRegion){lastRegion=region;window.chrome.webview.postMessage({type:'paperRegion',rects,viewportWidth:innerWidth,viewportHeight:innerHeight,pixelRatio:devicePixelRatio});}
    const settledKey=run.map(item=>Math.round(item.projectedHeight??item.node.getBoundingClientRect().height)).join(',');
    if(!busy&&!foldTweens.size&&run.length&&lastSettledCount!==settledKey){lastSettledCount=settledKey;window.chrome.webview.postMessage({type:'stackSettled',count:run.length,heights:run.map(item=>item.projectedHeight??item.node.getBoundingClientRect().height)});}
   }
   requestAnimationFrame(surfaceFrame);
  }
  let lastSettledCount=-1;
  requestAnimationFrame(surfaceFrame);

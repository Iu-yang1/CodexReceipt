  function isTearSwipe(start,point){
   const elapsed=point.time-start.time,dx=start.x-point.x,dy=Math.abs(start.y-point.y);
   return elapsed>0&&elapsed<=340&&dx>=96&&dx/elapsed>=.65&&dy<Math.max(28,dx*.3);
  }
  function clearTearGesture(){
   const gesture=tearGesture;tearGesture=null;
   if(gesture&&strip.hasPointerCapture?.(gesture.id))strip.releasePointerCapture(gesture.id);
  }
  strip.addEventListener('pointerdown',event=>{
   if(event.button!==0||busy||tearing||menuOpen)return;
   const item=run.find(item=>item.node===event.target||item.node.contains(event.target));
   if(!item)return;
   tearGesture={item,id:event.pointerId,x:event.clientX,y:event.clientY,time:performance.now()};
   // Capture keeps the release observable even if a fast drag leaves the clipped desktop window.
   strip.setPointerCapture(event.pointerId);
  });
  strip.addEventListener('pointermove',event=>{
   const gesture=tearGesture;
   if(!gesture||gesture.id!==event.pointerId)return;
   if(!(event.buttons&1)){clearTearGesture();return;}
   const point={x:event.clientX,y:event.clientY,time:performance.now()};
   if(isTearSwipe(gesture,point)){
    event.preventDefault();clearTearGesture();window.getSelection()?.removeAllRanges();
    void tearThrough(gesture.item);
   }
  });
  strip.addEventListener('pointerup',event=>{if(tearGesture?.id===event.pointerId){clearTearGesture();checkPointer();}});
  strip.addEventListener('pointercancel',clearTearGesture);
  strip.addEventListener('lostpointercapture',()=>{tearGesture=null;});

  async function tearThrough(selected){
   const end=run.indexOf(selected);
   if(end<0||busy||tearing)return false;
   const removed=run.slice(0,end+1),removedSet=new Set(removed);
   busy=true;tearing=true;hovered=null;hoverAnchor=null;update();
   for(const item of removed)foldTweens.delete(item);
   const bundle=document.createElement('div');bundle.className='tear-bundle';
   // Keep the same flow height throughout separation so newer receipts never jump.
   strip.insertBefore(bundle,removed[0].node);
   for(const item of removed)bundle.appendChild(item.node);
   const tearHeight=bundle.getBoundingClientRect().height;
   bundle.style.height=`${tearHeight}px`;
   let animation;
   try{
    if(sound.checked&&audioContext){
     const now=audioContext.currentTime;
     for(let i=0;i<5;i++)soundAt(now+i*.026,.022,.045,620+i*170);
    }
    if(!reduced()){
     const torn='polygon(0 0,100% 0,100% 100%,88% calc(100% - 1px),76% 100%,64% calc(100% - 3px),52% calc(100% - 2px),40% calc(100% - 5px),28% calc(100% - 4px),16% calc(100% - 8px),0 calc(100% - 10px))';
     animation=bundle.animate([
      {offset:0,transform:'translate(0,0)',clipPath:'polygon(0 0,100% 0,100% 100%,0 100%)',opacity:1},
      {offset:.22,transform:'translate(-16px,-2px) skewY(-.7deg)',clipPath:torn,opacity:1},
      {offset:.48,transform:'translate(-65px,-7px) skewY(-1.4deg)',clipPath:torn,opacity:1},
      {offset:1,transform:'translate(-340px,-22px) skewY(-2deg)',clipPath:torn,opacity:0}
     ],{duration:520,easing:'cubic-bezier(.35,0,.65,1)',fill:'forwards'});
     await animation.finished;
    }
   }catch(error){
    // An interrupted visual animation still completes the already requested tear.
    if(error.name!=='AbortError')console.warn('Receipt tear animation interrupted',error);
   }finally{
    if(animation)animation.cancel();
    for(const item of removed)item.node.remove();
    bundle.remove();run.splice(0,end+1);
    for(let i=history.length-1;i>=0;i--)if(removedSet.has(history[i]))history.splice(i,1);
    for(let i=markerUndo.length-1;i>=0;i--)if(removedSet.has(markerUndo[i].item))markerUndo.splice(i,1);
    busy=false;tearing=false;lastRegion='';lastSettledCount=-1;update();
    window.chrome.webview.postMessage({type:'torn',count:removed.length,remaining:run.length});
    if(queue.length)void drain();else checkPointer();
   }
   return true;
  }

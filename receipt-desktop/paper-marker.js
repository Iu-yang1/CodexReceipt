  const markerUndo=[];
  let markerSerial=0;
  function markerStroke(seed,width){
   // Vector ink follows the inline fragment, so wrapped text keeps its original layout.
   let state=seed>>>0;
   const random=()=>{state=(Math.imul(state,1664525)+1013904223)>>>0;return state/4294967296;};
   const w=Math.max(30,Math.min(320,width)),h=32,slope=(random()-.5)*2.8;
   const edge=(x,base)=>base+slope*(x/w-.5)+(random()-.5)*1.3;
   const upper=[],lower=[];
   for(let x=2;x<w-1;x+=2+random()*3){upper.push([x,edge(x,4)]);lower.unshift([x,edge(x,28)]);}
   const points=p=>p.map(([x,y])=>`${x.toFixed(2)},${y.toFixed(2)}`).join(' ');
   const body=`<polygon points="${points([[1.4,6],...upper,[w-1,5],[w-.8,25],...lower,[.7,26]])}" fill="#050504"/>`;
   // Dry bristles live outside the dense central stroke; paper shows only at its edges.
   let bristles='';
   for(let i=0;i<15;i++){
    const top=i%2===0,x=random()*w*.8,length=5+random()*w*.28;
    const y=edge(x,top?2.6:29.2),dy=slope*length/w;
    bristles+=`<path d="M${x.toFixed(2)} ${y.toFixed(2)}l${length.toFixed(2)} ${dy.toFixed(2)}" stroke="#080807" stroke-width="${(.25+random()*.7).toFixed(2)}"/>`;
   }
   const overpaint=`<path d="M1 16 Q${w*.35} 13 ${w-1} ${16+slope}" fill="none" stroke="#000" stroke-width="15" opacity=".8"/>`;
   const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none">${bristles}${body}${overpaint}</svg>`;
   return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
  }
  function refreshMarkedPaper(item,html){
   item.html=html;
   if(item.panels)item.node.querySelectorAll('.fold-slice>.paper,.reading-face>.paper,.curl-flat>.paper').forEach(paper=>paper.innerHTML=html);
   else item.node.innerHTML=html;
  }
  function censorSelection(){
   const selection=window.getSelection();
   if(!selection||selection.isCollapsed||!selection.rangeCount)return false;
   const range=selection.getRangeAt(0);
   const element=n=>n.nodeType===Node.ELEMENT_NODE?n:n.parentElement;
   const paper=element(range.startContainer)?.closest('.paper');
   if(!paper||paper!==element(range.endContainer)?.closest('.paper'))return false;
   const item=[...run,...(activeItem?[activeItem]:[])].find(item=>item.node===paper||item.node.contains(paper));
   if(!item||item===activeItem)return false;
   const selected=[];
   const walker=document.createTreeWalker(paper,NodeFilter.SHOW_TEXT);
   let text;
   while((text=walker.nextNode())){
    if(text.parentElement.closest('svg,.marker-censor')||!range.intersectsNode(text))continue;
    const start=text===range.startContainer?range.startOffset:0,end=text===range.endContainer?range.endOffset:text.length;
    if(end>start&&text.textContent.slice(start,end).trim())selected.push({text,start,end});
   }
   if(!selected.length)return false;
   markerUndo.push({item,html:item.html});
   if(markerUndo.length>30)markerUndo.shift();
   selected.reverse().forEach(({text,start,end},i)=>{
    if(end<text.length)text.splitText(end);
    const inked=start?text.splitText(start):text;
    const stroke=document.createElement('span');stroke.className='marker-censor';
    const inkRange=document.createRange();inkRange.selectNodeContents(inked);
    const width=Math.max(...[...inkRange.getClientRects()].map(rect=>rect.width),30);
    stroke.style.setProperty('--marker-stroke',markerStroke(++markerSerial*7919+i,width));
    inked.replaceWith(stroke);stroke.appendChild(inked);
   });
   const html=paper.innerHTML;selection.removeAllRanges();refreshMarkedPaper(item,html);
   window.chrome.webview.postMessage({type:'markerChanged',id:item.id,count:selected.length});
   return true;
  }
  document.addEventListener('keydown',event=>{
   if(event.code==='Space'&&!event.ctrlKey&&!event.metaKey&&!event.altKey){
    if(censorSelection())event.preventDefault();
   }
   if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='z'&&markerUndo.length){
    const undo=markerUndo.pop();if(undo.item.node.isConnected){event.preventDefault();refreshMarkedPaper(undo.item,undo.html);}
   }
  });
  // Development check exercises the real rendered selection/keyboard path, then restores the receipt.
  window.receiptMarkerSelfTest=()=>{
   const item=run.find(item=>item.node.querySelector('.hero-number'));
   if(!item)return {error:'No readable receipt'};
   const before=item.html;
   if(item.panels)commitFold(item,item.flatHeight);
   const face=item.node.querySelector('.reading-face>.paper')||item.node;
   const text=face.querySelector('.hero-number').firstChild;
   const selection=window.getSelection(),range=document.createRange();range.setStart(text,0);range.setEnd(text,Math.min(3,text.length));
   selection.removeAllRanges();selection.addRange(range);
   document.dispatchEvent(new KeyboardEvent('keydown',{code:'Space',key:' ',bubbles:true,cancelable:true}));
   const applied=item.html.includes('marker-censor');
   buildAccordion(item);commitFold(item,compactSize(item));commitFold(item,item.flatHeight);
   const survivesFold=!!item.node.querySelector('.reading-face .marker-censor');
   document.dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true,cancelable:true}));
   const restored=item.html===before;
   return {applied,survivesFold,restored};
  };
  let markerPreview=null;
  window.receiptMarkerPreview=(show=true)=>{
   if(!show){if(markerPreview){refreshMarkedPaper(markerPreview.item,markerPreview.html);markerPreview.item.holdUntil=markerPreview.holdUntil;markerUndo.length=markerPreview.undoLength;markerPreview=null;}return;}
   const item=run.find(item=>item.node.querySelector('.hero-number'));
   if(!item)return;
   markerPreview={item,html:item.html,holdUntil:item.holdUntil,undoLength:markerUndo.length};
   item.holdUntil=performance.now()+3000;
   if(item.panels)commitFold(item,item.flatHeight);
   const face=item.node.querySelector('.reading-face>.paper')||item.node;
   const range=document.createRange();range.setStartBefore(face.querySelector('.hero-label'));range.setEndAfter(face.querySelector('.detail:not(.pair)'));
   const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);
   document.dispatchEvent(new KeyboardEvent('keydown',{code:'Space',key:' ',bubbles:true,cancelable:true}));
  };

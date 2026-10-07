const fs=require('fs'),vm=require('vm'),assert=require('assert/strict');
let time=0,selectedCleared=false,drained=0,animationFrames;
const listeners={},messages=[];
const makeNode=()=>({isConnected:true,style:{},contains(target){return this===target;},remove(){this.isConnected=false;}});
const papers=Array.from({length:4},(_,i)=>({id:'#'+i,node:makeNode()}));
const run=[...papers],history=[...papers],markerUndo=papers.map(item=>({item}));
let finishAnimation;
const bundle={style:{},appendChild(){},getBoundingClientRect:()=>({height:400}),remove(){},animate(frames){animationFrames=frames;return {finished:new Promise(resolve=>finishAnimation=resolve),cancel(){}};}};
const context={run,history,markerUndo,busy:false,tearing:false,tearGesture:null,menuOpen:false,hovered:null,hoverAnchor:null,lastRegion:'old',lastSettledCount:4,
 foldTweens:new Map(),queue:[{}],sound:{checked:false},audioContext:null,
 strip:{addEventListener:(name,fn)=>listeners[name]=fn,setPointerCapture(){},hasPointerCapture:()=>false,insertBefore(){}},
 document:{createElement:()=>bundle},window:{getSelection:()=>({removeAllRanges:()=>selectedCleared=true}),chrome:{webview:{postMessage:m=>messages.push(m)}}},
 performance:{now:()=>time},update(){},reduced:()=>false,checkPointer(){},drain:()=>drained++};
vm.createContext(context);vm.runInContext(fs.readFileSync('paper-tear.js','utf8'),context);
const start={x:240,y:300,time:0};
assert(context.isTearSwipe(start,{x:110,y:306,time:160}));
assert(!context.isTearSwipe(start,{x:110,y:306,time:900}),'Slow text selection must remain selection');
assert(!context.isTearSwipe(start,{x:280,y:300,time:100}),'Right drag is not a tear');
assert(!context.isTearSwipe(start,{x:100,y:450,time:150}),'Vertical dragging is not a tear');
assert(!context.isTearSwipe(start,{x:190,y:300,time:60}),'Short selection is not a tear');
(async()=>{
 listeners.pointerdown({button:0,pointerId:1,target:papers[1].node,clientX:240,clientY:300});
 time=160;
 listeners.pointermove({pointerId:1,buttons:1,clientX:110,clientY:306,preventDefault(){}});
 assert(context.tearing&&context.busy&&selectedCleared);
 assert.equal(run.length,4,'Paper is retained during the tear animation');
 assert(animationFrames.at(-1).transform.includes('-340px'));
 finishAnimation();await new Promise(setImmediate);
 assert.deepEqual(run,[papers[2],papers[3]],'Selected and older papers removed; newer papers retained');
 assert.deepEqual(history,run);assert.deepEqual(markerUndo.map(x=>x.item),run);
 assert(!context.busy&&!context.tearing);assert.equal(drained,1);
 assert(messages.some(m=>m.type==='torn'&&m.count===2&&m.remaining===2));
 context.busy=true;
 assert.equal(await context.tearThrough(papers[2]),false,'Printing owns paper until feed finishes');
 console.log('PASS: fast left swipe, slow-selection protection, direction thresholds, torn prefix, retained newer papers, queue resume.');
})().catch(error=>{console.error(error);process.exitCode=1;});

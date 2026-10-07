const fs=require('fs'),vm=require('vm'),assert=require('assert/strict');
let now=0,frame;
const messages=[],listeners={},stripListeners={};
const run=[];
let nextId=0;
function item(h){const p={id:'#'+nextId++,flatHeight:h,projectedHeight:h,panels:null};p.node={isConnected:true,getBoundingClientRect(){const index=run.indexOf(p),bottom=760-run.slice(index+1).reduce((n,v)=>n+v.projectedHeight,0);return {left:0,right:320,top:bottom-p.projectedHeight,bottom,height:p.projectedHeight};}};return p;}
run.push(item(220),item(220));
const context={run,activeItem:null,busy:false,innerWidth:320,innerHeight:760,devicePixelRatio:1,performance:{now:()=>now},sound:{checked:false},tear:{click:()=>messages.push({type:'tear'})},
 strip:{addEventListener:(n,fn)=>stripListeners[n]=fn},
 window:{chrome:{webview:{postMessage:m=>messages.push(m),addEventListener:(n,fn)=>listeners[n]=fn}}},
 buildAccordion:p=>{p.panels??=[];},commitFold:(p,h)=>p.projectedHeight=h,
 requestAnimationFrame:fn=>frame=fn};
vm.createContext(context);
const folds=fs.readFileSync('paper-folds.js','utf8');vm.runInContext(folds.slice(0,folds.indexOf('  function buildAccordion')),context);
vm.runInContext(fs.readFileSync('paper-interactions.js','utf8')+'\nglobalThis.test={checkPointer,paperPrinted,paperRects};',context);
function tick(t){now=t;frame(t);}
function pointer(x,y){listeners.message({data:{type:'pointer',x,y}});}
const heights=run.map(context.compactSize);
run[1].holdUntil=5000;
tick(40);tick(450);assert.equal(run[0].projectedHeight,heights[0]);assert.equal(run[1].projectedHeight,220);
tick(4990);assert.equal(run[1].projectedHeight,220);
tick(5040);tick(5450);assert.equal(run[1].projectedHeight,heights[1]);
let r=context.test.paperRects()[0];pointer(100,r.y+r.height/2);tick(6000);
assert.equal(run[0].projectedHeight,220);assert.equal(run[1].projectedHeight,heights[1]);
pointer(-1,-1);tick(6200);assert(run[0].projectedHeight<220,'Folding begins immediately on pointer leave');tick(6410);assert.equal(run[0].projectedHeight,heights[0]);
assert(messages.some(m=>m.type==='paperRegion'&&m.rects.length===2));
context.busy=true;run[1].projectedHeight=220;run[1].feedAnimation={playState:'running'};tick(7000);tick(7450);assert.equal(run[1].projectedHeight,220);
context.busy=false;run[1].feedAnimation=null;tick(7500);tick(7920);assert.equal(run[1].projectedHeight,heights[1]);
// Hovering an older paper overrides a fresh paper's grace period, so neither is pushed outside.
run[1].projectedHeight=600;run[1].holdUntil=20000;r=context.test.paperRects()[0];pointer(100,r.y+r.height/2);
for(let t=8000;t<=8600;t+=40){tick(t);assert(run.reduce((sum,item)=>sum+item.projectedHeight,0)<=756.1);}
assert.equal(run[0].projectedHeight,220);assert.equal(run[1].projectedHeight,heights[1]);
listeners.message({data:{type:'action',action:'sound',enabled:true}});assert.equal(context.sound.checked,true);
listeners.message({data:{type:'action',action:'tear'}});assert(messages.some(m=>m.type==='tear'));
let prevented=false;
stripListeners.contextmenu({target:{closest:()=>true},preventDefault:()=>prevented=true,clientX:22,clientY:730});
assert(prevented&&messages.some(m=>m.type==='contextMenu'));
console.log('PASS: five-second new-paper grace, immediate pointer-leave folding, feed lock, hover keeps all papers inside viewport.');

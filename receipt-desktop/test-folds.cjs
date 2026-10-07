const fs=require('fs'),vm=require('vm'),assert=require('assert/strict');
const run=Array.from({length:12},(_,i)=>({id:'#'+i,flatHeight:500,projectedHeight:500,panels:[],node:{getBoundingClientRect:()=>({height:500}),animate:(frames)=>({frames})}}));
const context={run,innerHeight:760};vm.createContext(context);vm.runInContext(fs.readFileSync('paper-folds.js','utf8'),context);
const sizes=run.map(context.compactSize);
assert(sizes.every(n=>n>=8&&n<=14));assert(new Set(sizes).size>6);
assert.equal(context.compactSize(run[0]),sizes[0]);
for(const item of run){assert.equal(context.foldPose(item,context.compactSize(item)).face,0);assert.equal(context.foldPose(item,500).face,1);}
const plans=context.preparePushFolds(500);
assert(plans.length>1);assert(Math.abs(plans.reduce((s,p)=>s+p.amount,0)-500)<.001);
const animations=context.startPushFolds(plans,{stops:[{offset:0,progress:0},{offset:.5,progress:.5},{offset:1,progress:1}]},500,4200);
for(let i=0;i<3;i++){
 const consumed=animations.reduce((sum,a,j)=>sum+plans[j].start-parseFloat(a.frames[i].height),0);
 assert(consumed<=i*250+.001,'Folding cannot pull old paper backwards faster than new feed');
}
run.splice(1);let edgePlans=context.preparePushFolds(500);assert.equal(edgePlans.length,1);assert.equal(edgePlans[0].begin,258);assert.equal(edgePlans[0].amount,242);
run[0].projectedHeight=20;assert.equal(context.preparePushFolds(500).length,0);
for(let h=40;h<500;h+=30)assert.equal(context.foldPose(run[0],h).face,1,'Open faces remain opaque');
console.log('PASS: stable thickness, opaque folding, boundary trigger with only two papers, headroom and feed conservation.');

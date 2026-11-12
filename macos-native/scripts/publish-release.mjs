import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const repo=process.env.GH_REPO, tag=process.env.RELEASE_TAG, token=process.env.GH_TOKEN;
if(!repo||!tag||!token)throw new Error('GH_REPO, RELEASE_TAG and GH_TOKEN are required.');
const headers={Authorization:`Bearer ${token}`,Accept:'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'DailyLedger-release'};
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
async function api(url,options={}){
  const r=await fetch(url,{...options,headers:{...headers,...options.headers},signal:AbortSignal.timeout(120000)});
  if(!r.ok)throw new Error(`${options.method||'GET'} ${r.status}: ${url}`);
  return r.status===204?null:r.json();
}
const base=`https://api.github.com/repos/${repo}`;
let release=await api(`${base}/releases/tags/${encodeURIComponent(tag)}`);
if(release.draft)throw new Error('Create and publish the shared version Release first.');
const protectedAssets=release.assets.filter(a=>!/^(DailyLedger-.*-(?:macOS27-arm64|native-source)\.zip|(?:macOS-)?linked-frameworks\.txt|SHA256SUMS\.txt)$/.test(a.name));
const directory=path.join(root,'release');
const names=fs.readdirSync(directory).filter(n=>/^DailyLedger-.*-(?:macOS27-arm64|native-source)\.zip$/.test(n));
if(names.length!==2)throw new Error('Expected one Mac application archive and one native-source archive.');
async function put(name,bytes){
  const old=release.assets.find(a=>a.name===name);
  if(old?.digest===`sha256:${hash(bytes)}`)return;
  if(old){
    const response=await fetch(old.url,{headers:{...headers,Accept:'application/octet-stream'},signal:AbortSignal.timeout(120000)});
    if(!response.ok)throw new Error('Cannot back up existing asset.');
    const previous=Buffer.from(await response.arrayBuffer());
    if(old.digest&&old.digest!==`sha256:${hash(previous)}`)throw new Error('Previous asset digest mismatch.');
    const backups=path.join(directory,'previous-release-assets');fs.mkdirSync(backups,{recursive:true});fs.writeFileSync(path.join(backups,name),previous);
    await api(`${base}/releases/assets/${old.id}`,{method:'DELETE'});
  }
  const uploaded=await api(release.upload_url.replace(/\{.*$/,'')+'?name='+encodeURIComponent(name),{method:'POST',headers:{'Content-Type':'application/octet-stream'},body:bytes});
  if(uploaded.digest!==`sha256:${hash(bytes)}`)throw new Error('Uploaded asset digest mismatch.');
}
for(const name of names)await put(name,fs.readFileSync(path.join(directory,name)));
await put('macOS-linked-frameworks.txt',fs.readFileSync(path.join(directory,'linked-frameworks.txt')));
release=await api(`${base}/releases/${release.id}`);
const lines=[];
for(const a of release.assets.filter(a=>a.name!=='SHA256SUMS.txt').sort((a,b)=>a.name.localeCompare(b.name))){
  if(!a.digest?.startsWith('sha256:'))throw new Error('Missing server digest: '+a.name);
  lines.push(a.digest.slice(7)+'  '+a.name);
}
await put('SHA256SUMS.txt',Buffer.from(lines.join('\n')+'\n'));
const final=await api(`${base}/releases/${release.id}`);
for(const old of protectedAssets){if(!final.assets.some(a=>a.id===old.id&&a.name===old.name&&a.digest===old.digest))throw new Error('A non-Mac attachment changed unexpectedly.');}
console.log(`Updated Mac assets in ${tag}; preserved all existing non-Mac attachments. No separate Release was created.`);

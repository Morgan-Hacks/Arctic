"use client";
import {useCallback,useEffect,useRef,useState} from "react";
import {useRouter,useSearchParams} from "next/navigation";
import {directoryRequest,teamLabels,type DirectoryCard,type DirectoryProfile,type DirectoryStatus} from "@/lib/directory";
import {HackerCard} from "./hacker-card";

type Page={items:DirectoryCard[];nextCursor:string|null};
export function BrowseDirectory({status}:{status:DirectoryStatus}){
 const params=useSearchParams();const router=useRouter();const [query,setQuery]=useState(params.get("q")??"");
 const [page,setPage]=useState<Page>({items:[],nextCursor:null});const [busy,setBusy]=useState(true);const [more,setMore]=useState(false);const [error,setError]=useState("");const [reload,setReload]=useState(0);
 const [detail,setDetail]=useState<DirectoryProfile|null>(null);const [detailError,setDetailError]=useState("");const [copied,setCopied]=useState("");const dialog=useRef<HTMLDialogElement>(null);const opener=useRef<HTMLButtonElement|null>(null);
 const all=params.toString();const filters=new URLSearchParams(all);filters.delete("h");const filterKey=filters.toString();const selected=params.get("h");
 const update=useCallback((key:string,value:string)=>{const p=new URLSearchParams(window.location.search);value?p.set(key,value):p.delete(key);p.delete("h");router.replace(`/portal/directory${p.size?`?${p}`:""}`,{scroll:false})},[router]);
 useEffect(()=>setQuery(params.get("q")??""),[params]);
 useEffect(()=>{if(query===(params.get("q")??""))return;const timer=setTimeout(()=>update("q",query.trim()),300);return()=>clearTimeout(timer)},[query,params,update]);
 useEffect(()=>{const controller=new AbortController();setBusy(true);setError("");setPage({items:[],nextCursor:null});
 directoryRequest<Page>(`?${filterKey}`,{signal:controller.signal}).then(setPage).catch(e=>{if(e.name!=="AbortError")setError(e.message)}).finally(()=>{if(!controller.signal.aborted)setBusy(false)});return()=>controller.abort()},[filterKey,reload]);
 useEffect(()=>{if(!selected){dialog.current?.close();opener.current?.focus();setDetail(null);return}const controller=new AbortController();setDetail(null);setDetailError("");setCopied("");dialog.current?.showModal();
 directoryRequest<DirectoryProfile>(`/${encodeURIComponent(selected)}`,{signal:controller.signal}).then(setDetail).catch(e=>{if(e.name!=="AbortError")setDetailError(e.message)});return()=>controller.abort()},[selected]);
 function close(){const p=new URLSearchParams(window.location.search);p.delete("h");router.replace(`/portal/directory${p.size?`?${p}`:""}`,{scroll:false})}
 async function loadMore(){if(!page.nextCursor)return;const key=filterKey;setMore(true);setError("");try{const result=await directoryRequest<Page>(`?${key}&cursor=${encodeURIComponent(page.nextCursor)}`);const current=new URLSearchParams(window.location.search);current.delete("h");if(current.toString()===key)setPage(p=>({items:[...p.items,...result.items.filter(i=>!p.items.some(old=>old.id===i.id))],nextCursor:result.nextCursor}))}catch(e){setError((e as Error).message)}finally{setMore(false)}}
 return <><div className="directory-filters"><label>Search hackers<input type="search" value={query} maxLength={100} onChange={e=>setQuery(e.target.value)}/></label>
 <label>Skills<select multiple value={(params.get("skills")??"").split(",")} onChange={e=>update("skills",Array.from(e.target.selectedOptions,o=>o.value).join(","))}>{status.catalogues.skills.map(v=><option key={v}>{v}</option>)}</select></label>
 <label>Team status<select value={params.get("status")??""} onChange={e=>update("status",e.target.value)}><option value="">All</option>{status.catalogues.teamStatuses.map(v=><option key={v} value={v}>{teamLabels[v]}</option>)}</select></label>
 <label>Weekend pace<select value={params.get("pace")??""} onChange={e=>update("pace",e.target.value)}><option value="">All</option>{status.catalogues.paces.map(v=><option key={v}>{v}</option>)}</select></label>
 <label>First hackathon<select value={params.get("firstTimer")??""} onChange={e=>update("firstTimer",e.target.value)}><option value="">All</option><option value="true">Yes</option><option value="false">No</option></select></label>
 <button onClick={()=>{setQuery("");router.replace("/portal/directory",{scroll:false})}}>Clear filters</button></div>
 {error?<div role="alert"><p>{error}</p><button onClick={()=>setReload(v=>v+1)}>Try again</button></div>:null}
 {busy?<div role="status"><p>Loading hackers…</p><div className="directory-grid">{[1,2,3].map(i=><div key={i} className="panel directory-skeleton"/>)}</div></div>:<>
 {!error&&page.items.length===0?<p>No hackers match these filters. Try clearing the filters.</p>:null}
 {page.items.length>0&&page.items.length<10&&!page.nextCursor&&!filterKey?<p className="quiet">More hackers join as RSVPs come in.</p>:null}
 <div className="directory-grid">{page.items.map(p=><button key={p.id} className="directory-card-button" aria-label={`View ${p.displayName}'s profile`} onClick={e=>{opener.current=e.currentTarget;const q=new URLSearchParams(all);q.set("h",p.id!);router.push(`/portal/directory?${q}`,{scroll:false})}}><HackerCard profile={p}/></button>)}</div>
 {page.nextCursor?<button disabled={more} onClick={()=>void loadMore()}>{more?"Loading…":"Load more"}</button>:null}</>}
 <dialog className="directory-sheet" ref={dialog} aria-label="Hacker profile" onCancel={e=>{e.preventDefault();close()}}><button autoFocus onClick={close}>Close profile</button>
 {detailError?<p role="alert">{detailError}</p>:detail?<><HackerCard profile={detail}/><p>{detail.pronouns}</p><dl><dt>What I want to build</dt><dd>{detail.buildIdea||"Not specified"}</dd><dt>Looking for in teammates</dt><dd>{detail.lookingFor||"Not specified"}</dd><dt>Interests</dt><dd>{detail.interests.join(", ")||"Not specified"}</dd><dt>Skills</dt><dd>{detail.skills.join(", ")}</dd><dt>First hackathon</dt><dd>{detail.firstHackathon===null?"Not specified":detail.firstHackathon?"Yes":"No"}</dd></dl><h2>Get in touch</h2>
 {detail.contactLinks.map(l=>l.kind==="discord"?<p key={l.kind}>Discord: {l.value} <button onClick={async()=>{try{await navigator.clipboard.writeText(l.value);setCopied("Discord username copied.")}catch{setCopied("Copy the Discord username shown above.")}}}>Copy username</button></p>:<p key={l.kind}><a href={l.kind==="github"?`https://github.com/${l.value}`:l.value} target="_blank" rel="noopener noreferrer">{l.kind}</a></p>)}<p role="status">{copied}</p></>:<p role="status">Loading profile…</p>}
 </dialog></>
}

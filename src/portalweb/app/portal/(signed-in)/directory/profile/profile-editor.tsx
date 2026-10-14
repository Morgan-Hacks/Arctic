"use client";
import {useEffect,useState} from "react";
import Link from "next/link";
import {directoryRequest,teamLabels,type DirectoryProfile,type DirectoryStatus,type OwnProfile} from "@/lib/directory";
import {TeammateField} from "./teammate-field";
import {HackerCard} from "../hacker-card";

export function ProfileEditor({status,initial}:{status:DirectoryStatus;initial:OwnProfile}){
 const [own,setOwn]=useState(initial);const [profile,setProfile]=useState(initial.profile);
 const [busy,setBusy]=useState(false);const [error,setError]=useState("");const [message,setMessage]=useState("");const [errors,setErrors]=useState<Record<string,string[]>>({});
 const dirty=JSON.stringify(profile)!==JSON.stringify(own.profile);const archived=status.phase==="archived";
 useEffect(()=>{if(!dirty)return;const unload=(e:BeforeUnloadEvent)=>{e.preventDefault();e.returnValue=""};
 const leave=(e:MouseEvent)=>{const a=(e.target as Element).closest("a");if(a&&a.href!==location.href&&!window.confirm("Leave without saving your profile?"))e.preventDefault()};
 window.addEventListener("beforeunload",unload);document.addEventListener("click",leave,true);return()=>{window.removeEventListener("beforeunload",unload);document.removeEventListener("click",leave,true)}},[dirty]);
 async function action(path:string,init:RequestInit={method:"POST"}){setBusy(true);setError("");setErrors({});setMessage("");try{const result=await directoryRequest<OwnProfile>(path,init);setOwn(result);setProfile(result.profile);setMessage("Saved.")}catch(e){setError((e as Error).message);setErrors((e as {fields?:Record<string,string[]>}).fields??{})}finally{setBusy(false)}}
 function set<K extends keyof DirectoryProfile>(key:K,value:DirectoryProfile[K]){if(profile)setProfile({...profile,[key]:value})}
 async function photo(file:File|undefined){if(!file)return;if(file.size>5*1024*1024){setError("Choose a photo up to 5 MB.");return}setBusy(true);setError("");try{const form=new FormData();form.set("file",file);const result=await directoryRequest<{photoId:string;previewUrl:string}>("/profile/photo",{method:"POST",body:form});setProfile(p=>p?{...p,photoId:result.photoId,photoUrl:result.previewUrl}:p)}catch(e){setError((e as Error).message)}finally{setBusy(false)}}
 function field(key:"displayName"|"school"|"pronouns"|"headline"|"buildIdea",label:string,max:number){return <div className="directory-field" key={key}><label htmlFor={key}>{label}</label><input id={key} value={profile?.[key]??""} maxLength={max} onChange={e=>set(key,e.target.value)} aria-invalid={!!errors[key]} aria-describedby={`${key}-count ${key}-error`} /><small id={`${key}-count`}>{profile?.[key]?.length??0} / {max}</small><span id={`${key}-error`} className="directory-error">{errors[key]?.join(" ")}</span></div>}
 function choices(key:"skills"|"interests",label:string,values:string[]){return <fieldset aria-describedby={`${key}-error`}><legend>{label} (up to 5)</legend><div className="directory-options">{values.map(v=><label key={v}><input type="checkbox" checked={profile?.[key].includes(v)??false} disabled={!profile?.[key].includes(v)&&(profile?.[key].length??0)>=5} onChange={e=>set(key,e.target.checked?[...(profile?.[key]??[]),v]:(profile?.[key]??[]).filter(s=>s!==v))}/>{v}</label>)}</div><span id={`${key}-error`} className="directory-error">{errors[key]?.join(" ")}</span></fieldset>}
 return <><Link href="/portal/directory">Browse hackers</Link>
 {archived?<p className="notice">The directory has closed. Your profile is read-only.</p>:status.phase==="notOpen"?<p className="notice">{status.opensAt?`Your complete, opted-in profile appears on ${new Date(status.opensAt).toLocaleDateString()}.`:"The opening date has not been announced. You can prepare your profile now."}</p>:null}
 {own.state==="hidden"?<p className="notice">Organizers have hidden your profile: {own.hiddenReason}. You can still edit it.</p>:null}
 {own.photoRemoved?<p className="notice">Organizers removed your photo. You can upload another.</p>:null}
 {own.missingFields.length>0?<p className="notice">To appear in the directory, complete: {own.missingFields.map(s=>({displayName:"display name",school:"school",skills:"skills",contactLinks:"contact links"}[s]??s)).join(", ")}.</p>:null}
 {error?<p role="alert" className="directory-error">{error}</p>:null}<p role="status">{message}</p>
 {!profile?<button disabled={busy||archived} onClick={()=>action("/profile/opt-in")}>Join the hacker directory</button>:<>
 <label className="directory-optin"><input type="checkbox" checked={own.optedIn} disabled={busy||archived} onChange={e=>{if(!dirty||window.confirm("Discard unsaved edits and change visibility?"))void action(e.target.checked?"/profile/opt-in":"/profile/opt-out")}}/>Show me in the hacker directory</label><p className="quiet">Only RSVP’d hackers can see your complete profile. Opting out keeps your saved details.</p>
 <div className="directory-editor"><form onSubmit={e=>{e.preventDefault();void action("/profile",{method:"PUT",body:JSON.stringify(profile)})}}><fieldset disabled={busy||archived} className="directory-form">
 {field("displayName","Display name",200)}{field("school","School",200)}<div className="directory-field">
 <label htmlFor="yearLevel">Class classification</label>
 <select id="yearLevel" value={status.catalogues.classifications.includes(profile.yearLevel??"")?profile.yearLevel??"":""} onChange={e=>set("yearLevel",e.target.value||null)} aria-invalid={!!errors.yearLevel} aria-describedby="yearLevel-help yearLevel-error">
 <option value="">Select your classification</option>
 {status.catalogues.classifications.map(value=><option key={value} value={value}>{value}</option>)}
 </select>
 <small id="yearLevel-help">{profile.yearLevel&&!status.catalogues.classifications.includes(profile.yearLevel)?"Choose a classification to replace your previously saved year or level.":"Choose your current class classification."}</small>
 <span id="yearLevel-error" className="directory-error">{errors.yearLevel?.join(" ")}</span>
 </div>{field("pronouns","Pronouns",30)}{field("headline","Headline",160)}
 {choices("skills","Skills",status.catalogues.skills)}{choices("interests","Interests or tracks",status.catalogues.interests)}
 {field("buildIdea","What I want to build",280)}<TeammateField value={profile.lookingFor??""} errors={errors.lookingFor} onChange={value=>set("lookingFor",value)}/>
 <label>First hackathon<select value={profile.firstHackathon===null?"":String(profile.firstHackathon)} onChange={e=>set("firstHackathon",e.target.value===""?null:e.target.value==="true")}><option value="">Not specified</option><option value="true">Yes</option><option value="false">No</option></select></label>
 <label>Weekend pace<select value={profile.pace??""} onChange={e=>set("pace",e.target.value||null)}><option value="">Not specified</option>{status.catalogues.paces.map(v=><option key={v}>{v}</option>)}</select></label><span className="directory-error">{errors.pace?.join(" ")}</span>
 <fieldset><legend>Team status</legend>{status.catalogues.teamStatuses.map(v=><label key={v}><input type="radio" name="teamStatus" value={v} checked={profile.teamStatus===v} onChange={()=>setProfile({...profile,teamStatus:v,teammatesNeeded:v==="needMembers"?1:null})}/>{teamLabels[v]}</label>)}</fieldset>
 {profile.teamStatus==="needMembers"?<label>Teammates needed<select value={profile.teammatesNeeded??1} onChange={e=>set("teammatesNeeded",Number(e.target.value))}>{Array.from({length:status.catalogues.maxTeamSize-1},(_,i)=><option key={i} value={i+1}>{i+1}</option>)}</select><span className="directory-error">{errors.teammatesNeeded?.join(" ")}</span></label>:null}
 <fieldset aria-describedby="contactLinks-error"><legend>Contact links (up to 3)</legend>{profile.contactLinks.map((link,i)=><div className="directory-contact" key={i}><label>Kind<select value={link.kind} onChange={e=>set("contactLinks",profile.contactLinks.map((v,n)=>n===i?{...v,kind:e.target.value}:v))}>{status.catalogues.linkKinds.map(k=><option key={k} disabled={profile.contactLinks.some((v,n)=>n!==i&&v.kind===k)}>{k}</option>)}</select></label><label>Username or URL<input value={link.value} maxLength={200} onChange={e=>set("contactLinks",profile.contactLinks.map((v,n)=>n===i?{...v,value:e.target.value}:v))}/><small>{link.value.length} / 200</small></label><button type="button" onClick={()=>set("contactLinks",profile.contactLinks.filter((_,n)=>n!==i))}>Remove contact {i+1}</button></div>)}<button type="button" disabled={profile.contactLinks.length>=3} onClick={()=>set("contactLinks",[...profile.contactLinks,{kind:status.catalogues.linkKinds.find(k=>!profile.contactLinks.some(v=>v.kind===k))??"discord",value:""}])}>Add contact</button><span id="contactLinks-error" className="directory-error">{errors.contactLinks?.join(" ")}</span></fieldset>
 <label>Profile photo<input type="file" accept="image/jpeg,image/png,image/webp" onChange={e=>void photo(e.target.files?.[0])}/></label><p className="quiet">Up to 5 MB. We crop your photo to a square and remove metadata.</p>{profile.photoId?<button type="button" onClick={()=>setProfile({...profile,photoId:null,photoUrl:null})}>Remove photo</button>:null}
 <button type="submit" className="primary">{busy?"Saving…":"Save profile"}</button></fieldset></form><aside><h2>Card preview</h2><HackerCard profile={profile}/><p className="quiet">{own.state==="live"&&status.phase==="open"?"Your saved profile is live.":"This preview is only visible to you."}</p></aside></div></>}
 </>
}

"use client";
import {useEffect,useState} from "react";
import Link from "next/link";
import {directoryRequest,type DirectoryStatus,type OwnProfile} from "@/lib/directory";
export function JoinDirectoryCard(){const [show,setShow]=useState(false);const key="morganhacks-directory-dismissed";
 useEffect(()=>{let active=true;try{if(localStorage.getItem(key))return}catch{}directoryRequest<DirectoryStatus>("/status").then(async s=>{if(!s.eligible||s.phase==="archived")return;const own=await directoryRequest<OwnProfile>("/profile");if(active)setShow(!own.optedIn)}).catch(()=>{});return()=>{active=false}},[]);
 return show?<section className="panel"><h2>Join the hacker directory</h2><p>Help other RSVP’d hackers find you as a teammate.</p><Link href="/portal/directory/profile">Set up your profile</Link> <button type="button" className="link" onClick={()=>{try{localStorage.setItem(key,"true")}catch{}setShow(false)}}>Dismiss</button></section>:null}

import type {DirectoryCard} from "@/lib/directory";
import {teamLabels} from "@/lib/directory";
export function HackerCard({profile}:{profile:DirectoryCard}){return <article className="hacker-card">
 {profile.photoUrl?<img src={profile.photoUrl} width={80} height={80} alt={`Photo of ${profile.displayName??"hacker"}`} />:<div className="hacker-avatar" aria-hidden="true">{profile.displayName?.slice(0,1)??"?"}</div>}
 <h2>{profile.displayName||"Your name"}</h2><p>{[profile.school,profile.yearLevel].filter(Boolean).join(" · ")||"Your school"}</p><p>{profile.headline}</p>
 <ul className="directory-tags">{profile.skills.slice(0,3).map(s=><li key={s}>{s}</li>)}</ul>
 <p>{teamLabels[profile.teamStatus??""]??"Team status not set"}{profile.teamStatus==="needMembers"&&profile.teammatesNeeded?` · ${profile.teammatesNeeded} needed`:""}</p><p className="quiet">{profile.pace}</p></article>}

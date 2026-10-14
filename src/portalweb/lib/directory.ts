export type ContactLink={kind:string;value:string};
export type DirectoryProfile={id?:string;displayName:string|null;school:string|null;yearLevel:string|null;firstHackathon:boolean|null;pronouns:string|null;headline:string|null;buildIdea:string|null;lookingFor:string|null;pace:string|null;teamStatus:string|null;teammatesNeeded:number|null;skills:string[];interests:string[];contactLinks:ContactLink[];photoId:string|null;photoUrl?:string|null};
export type DirectoryCard=Pick<DirectoryProfile,"id"|"displayName"|"school"|"yearLevel"|"headline"|"skills"|"teamStatus"|"teammatesNeeded"|"pace"|"photoUrl">;
export type DirectoryStatus={enabled:boolean;phase:"notOpen"|"open"|"archived";opensAt:string|null;eligible:boolean;reason:string|null;catalogues:{skills:string[];paces:string[];linkKinds:string[];teamStatuses:string[];interests:string[];maxTeamSize:number;classifications:string[]}};
export type OwnProfile={profile:DirectoryProfile|null;state:string;optedIn:boolean;missingFields:string[];photoUrl:string|null;hiddenReason:string|null;photoRemoved:boolean};
export const teamLabels:Record<string,string>={looking:"Looking for a team",needMembers:"Need members",full:"Team is full",solo:"Working solo"};
export async function directoryRequest<T>(path:string,init:RequestInit={}):Promise<T>{
 const response=await fetch(`/api/portal/directory${path}`,{...init,headers:{...(typeof init.body==="string"?{"content-type":"application/json"}:{}),...init.headers}});
 if(response.status===401){window.location.assign("/portal/sign-in");throw new Error("Please sign in again.")}
 const data=await response.json().catch(()=>({}));
 if(!response.ok)throw Object.assign(new Error(data.error??"The directory could not be loaded. Try again."),{fields:data.errors??{}});
 return data as T;
}

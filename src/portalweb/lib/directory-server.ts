import {apiFetch} from "./api";
import {isOn} from "./features";
import {notFound,redirect} from "next/navigation";
import type {DirectoryStatus,OwnProfile} from "./directory";
export const DIRECTORY="enable_directory_feature";
export async function directoryContext(){
 if(!isOn(DIRECTORY))notFound();
 const response=await apiFetch("/portal/directory/status");
 if(response.status===401)redirect("/portal/sign-in");
 if(response.status===404)notFound();
 if(!response.ok)throw new Error("The directory could not be loaded.");
 const status=await response.json() as DirectoryStatus;
 let own:OwnProfile|null=null;
 if(status.eligible){const profile=await apiFetch("/portal/directory/profile");if(!profile.ok)throw new Error("Your directory profile could not be loaded.");own=await profile.json() as OwnProfile;}
 return {status,own};
}

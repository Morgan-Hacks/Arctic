import {directoryContext} from "@/lib/directory-server";
import {ProfileEditor} from "./profile-editor";
export default async function ProfilePage(){const {status,own}=await directoryContext();return <><h1>Your hacker profile</h1><p>Help other hackers find you as a teammate.</p>{status.eligible&&own?<ProfileEditor status={status} initial={own}/>:<p className="notice">{status.reason}</p>}</>}

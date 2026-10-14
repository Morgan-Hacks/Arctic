export const dynamic = "force-dynamic";
import {notFound,redirect} from "next/navigation";
import {apiFetch,currentPerson} from "@/lib/api";
import {isOn} from "@/lib/features";
import {Shell} from "../shell";
import {DirectoryConsole} from "./directory-console";
export default async function DirectoryPage(){
 if(!isOn("enable_directory_feature"))notFound();
 const person=await currentPerson();if(!person)redirect("/sign-in");
 const response=await apiFetch("/admin/directory/events");
 if(response.status===401)redirect("/sign-in");
 if(response.status===404)notFound();
 if(!response.ok)return <Shell personId={person.personId}><h1>Hacker directory</h1><p>{response.status===403?"Organizer access is required.":"Events could not be loaded. Please try again."}</p></Shell>;
 const {events}=await response.json() as {events:{id:string;name:string}[]};
 return <Shell personId={person.personId}><h1>Hacker directory</h1><DirectoryConsole events={events}/></Shell>
}

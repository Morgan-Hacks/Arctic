"use client";
export default function Error({reset}:{reset:()=>void}){return <div role="alert" className="notice problem"><p>The directory could not be loaded.</p><button onClick={reset}>Try again</button></div>}

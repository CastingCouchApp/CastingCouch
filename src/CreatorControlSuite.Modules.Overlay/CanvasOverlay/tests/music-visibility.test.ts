// @vitest-environment jsdom
import { expect, it } from "vitest";
import { createSpotifyEl, updateSpotify } from "../src/shared/widgets/music";
import type { LayoutItem } from "../src/shared/types";
const item:LayoutItem={id:"music",kind:"widget",type:"music",x:0,y:0,w:950,h:188,z:1,props:{}};
it("respects authoritative server visibility during the global pause grace period",()=> {
    const el=createSpotifyEl(item);
    updateSpotify(el,item,{music:{connected:true,title:"Song",isPlaying:false,hideWhenPaused:true,showInOverlay:true,visible:true}});
    expect(el.classList.contains("visible")).toBe(true);
    updateSpotify(el,item,{music:{connected:true,title:"Song",isPlaying:false,hideWhenPaused:true,showInOverlay:false,visible:false}});
    expect(el.classList.contains("visible")).toBe(false);
});
it("preserves explicit widget pause hiding and legacy payload fallback",()=> {
    const el=createSpotifyEl(item);
    updateSpotify(el,{...item,props:{hideWhenPaused:true}},{music:{connected:true,title:"Song",isPlaying:false,hideWhenPaused:false,showInOverlay:true,visible:true}});
    expect(el.classList.contains("visible")).toBe(false);
    updateSpotify(el,item,{spotify:{connected:true,title:"Song",isPlaying:false,hideWhenPaused:true}});
    expect(el.classList.contains("visible")).toBe(false);
});

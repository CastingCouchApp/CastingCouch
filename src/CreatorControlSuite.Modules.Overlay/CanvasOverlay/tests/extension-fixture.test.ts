import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { expect, it, vi } from "vitest";
import { loadExtensions, extUrl } from "../src/shared/extensions/loader";
import { registerWidget } from "../src/shared/extensions/registry";
import { registerEffect, EFFECT_STRATEGIES } from "../src/shared/effects/registry";
import { registerAnimation, ANIMATION_STRATEGIES } from "../src/shared/animations/registry";
import { createItemContent, updateRegisteredWidget } from "../src/shared/runtime/item-content";
import { paletteEntriesFromPacks } from "../src/editor/shell/pack-palette";
import type { LayoutItem } from "../src/shared/types";

it("fetches and executes the actual C# fixture scripts through the shared loader and runtime", async () => {
  const fixture = new URL("../../../../tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/", import.meta.url);
  const manifest = JSON.parse(await readFile(new URL("manifest.json",fixture),"utf8"));
  const resources = new Map<string,Buffer>();
  for (const path of ["widgets/banner/index.js","effects/sparkle/index.js","animations/wobble/index.js","fonts/CoolFont.woff2","assets/icons/logo.svg"]) {
    resources.set(`/ext/cool-kit/${path}`,await readFile(new URL(path,fixture)));
  }
  const requests:string[]=[];
  const server=createServer((req,res)=> {
    requests.push(req.url || "");
    if (req.url==="/extensions") { res.setHeader("Content-Type","application/json"); res.end(JSON.stringify({packs:[manifest]})); }
    else if (resources.has(req.url || "")) res.end(resources.get(req.url!));
    else { res.statusCode=404; res.end(); }
  });
  await new Promise<void>(resolve=>server.listen(0,"127.0.0.1",resolve));
  const address=server.address(); if (!address || typeof address==="string") throw Error("Missing server port");
  const base=`http://127.0.0.1:${address.port}`;
  const dom=new JSDOM("<!doctype html><html><head></head><body></body></html>",{url:`${base}/editor/default`,runScripts:"dangerously"});
  const actualFetch=globalThis.fetch;
  Object.assign(dom.window,{CcsCanvas:{registerWidget,registerEffect,registerAnimation,extUrl}});
  vi.stubGlobal("window",dom.window); vi.stubGlobal("document",dom.window.document); vi.stubGlobal("location",dom.window.location);
  vi.stubGlobal("fetch",actualFetch);
  try {
    const packs=await loadExtensions();
    expect(packs[0].id).toBe("cool-kit");
    expect(requests).toContain("/ext/cool-kit/widgets/banner/index.js");
    expect(requests).toContain("/ext/cool-kit/effects/sparkle/index.js");
    expect(requests).toContain("/ext/cool-kit/animations/wobble/index.js");
    const item:LayoutItem={id:"fixture",kind:"widget",type:"ext:cool-kit:banner",x:0,y:0,w:400,h:120,z:1,props:{text:"Imported banner"}};
    const element=createItemContent(item);
    expect(element.className).toBe("cool-kit-banner");
    expect(updateRegisteredWidget(element,item)).toBe(true);
    expect(element.textContent).toBe("Imported banner");
    const layer=dom.window.document.createElement("div");
    EFFECT_STRATEGIES["ext:cool-kit:sparkle"].apply(layer,{type:"ext:cool-kit:sparkle",enabled:true,settings:{}},item);
    expect(layer.classList.contains("cool-kit-sparkle")).toBe(true);
    ANIMATION_STRATEGIES["ext:cool-kit:wobble"].apply(element,{type:"ext:cool-kit:wobble",enabled:true,settings:{durationMs:450,intensity:0.8}},item);
    expect(element.style.animation).toContain("cool-kit-wobble 450ms");
    expect(element.style.getPropertyValue("--cool-kit-wobble-i")).toBe("0.8");
    expect(paletteEntriesFromPacks(packs)[0]).toMatchObject({type:"ext:cool-kit:banner",label:"Cool Banner",category:"Extension · Cool Kit"});
    expect(dom.window.document.querySelector("style[data-ccs-ext-fonts]")?.textContent).toContain(`${base}/ext/cool-kit/fonts/CoolFont.woff2`);
    expect(await (await actualFetch(`${base}/ext/cool-kit/assets/icons/logo.svg`)).text()).toContain("<svg");
  } finally {
    vi.unstubAllGlobals(); dom.window.close();
    server.closeAllConnections(); await new Promise<void>(resolve=>server.close(()=>resolve()));
  }
});

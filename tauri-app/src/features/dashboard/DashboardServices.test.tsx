import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createMemoryHistory, createRootRoute, createRoute, createRouter, RouterProvider } from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { DashboardServices } from "./DashboardServices";
import { defaultAppSettings } from "../../lib/app-settings";
import { cardVisible } from "./dashboard-types";
import draft from "./dashboard-default.json";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({ tauriInvoke: (cmd:string, args:unknown) => invoke(cmd,args), queryKeys:{services:["services"],musicPlayer:["music-player"],alertRuntime:["alert-runtime"],alerts:["alerts"]}, FALLBACK_POLL_MS:15000 }));
beforeEach(() => { invoke.mockReset(); invoke.mockImplementation(async(cmd) => cmd === "list_alerts" ? [{type:"Follow",enabled:true},{type:"Raid",enabled:false}] : {}); });
function show(provider="spotify", state="disconnected") {
    const settings=defaultAppSettings(); settings.MusicPlayer.Source=provider;
    const root=createRootRoute();
    const route=createRoute({getParentRoute:()=>root,path:"/",component:()=> <DashboardServices settings={settings} services={[{id:"obs",name:"OBS",state:state as "connected",detail:""},{id:"twitch",name:"Twitch",state:"disconnected",detail:""},{id:provider,name:provider,state:"disconnected",detail:""}]}/>});
    const client=new QueryClient({defaultOptions:{queries:{retry:false}}});
    render(<QueryClientProvider client={client}><RouterProvider router={createRouter({routeTree:root.addChildren([route]),history:createMemoryHistory({initialEntries:["/"]})})}/></QueryClientProvider>);
    return client;
}
it("launches the saved application separately from connecting and exposes failed launch with retry", async()=>{
    const client=show(); const invalidate=vi.spyOn(client,"invalidateQueries");
    const card=await screen.findByRole("region",{name:"OBS Studio"});
    invoke.mockRejectedValueOnce(new Error("Programmpfad fehlt"));
    fireEvent.click(within(card).getByRole("button",{name:"Starten"}));
    expect(await screen.findByText("Programmpfad fehlt")).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith("launch_service",{service:"obs"});
    expect(invoke).not.toHaveBeenCalledWith("connect_obs",undefined);
    invoke.mockResolvedValueOnce({status:"already_running",message:"OBS läuft bereits."});
    fireEvent.click(within(card).getByRole("button",{name:"Starten"}));
    expect(await screen.findByText("OBS läuft bereits.")).toBeInTheDocument();
    expect(screen.queryByText("Programmpfad fehlt")).not.toBeInTheDocument();
    fireEvent.click(within(card).getByRole("button",{name:"Verbinden"}));
    await waitFor(()=>expect(invoke).toHaveBeenCalledWith("connect_obs",undefined));
    expect(invalidate).toHaveBeenCalledWith({queryKey:["services"]});
});
it("uses the selected music provider, prevents reconnect during connection and links to retained pages", async()=>{
    show("ytmusic","connecting");
    const obs=await screen.findByRole("region",{name:"OBS Studio"});
    expect(within(obs).getByRole("button",{name:"Verbinden …"})).toBeDisabled();
    const music=screen.getByRole("region",{name:"YouTube Music"});
    expect(within(music).queryByRole("button",{name:"Starten"})).not.toBeInTheDocument();
    fireEvent.click(within(music).getByRole("button",{name:"Verbinden"}));
    await waitFor(()=>expect(invoke).toHaveBeenCalledWith("music_player_connect",undefined));
    expect(screen.getByRole("link",{name:"Musikplayer öffnen"})).toHaveAttribute("href","/music");
    expect(screen.getByRole("link",{name:"Twitch öffnen"})).toHaveAttribute("href","/services#twitch");
    expect(screen.getByRole("link",{name:"Alerts öffnen"})).toHaveAttribute("href","/alerts");
    expect(screen.getByRole("link",{name:"Overlay öffnen"})).toHaveAttribute("href","/overlay");
    expect(screen.queryByText(/Streamer.bot|Stream Deck|Workflow/)).not.toBeInTheDocument();
});
it("tests the selected enabled alert through the existing native engine and preserves QuickServices visibility", async()=>{
    show(); await screen.findByRole("region",{name:"Alerts"});
    expect(await screen.findByRole("option",{name:"Follow"})).toBeInTheDocument();
    expect(screen.queryByRole("option",{name:"Raid"})).not.toBeInTheDocument();
    invoke.mockResolvedValueOnce(1);
    fireEvent.click(screen.getByRole("button",{name:"Test-Alert"}));
    await waitFor(()=>expect(invoke).toHaveBeenCalledWith("test_alert",{alertType:"Follow",user:"Test User"}));
    expect(await screen.findByText("Test-Alert in die Queue aufgenommen.")).toBeInTheDocument();
    invoke.mockResolvedValueOnce(0);
    fireEvent.click(screen.getByRole("button",{name:"Test-Alert"}));
    expect(await screen.findByText("Kein Alert aufgenommen. Engine oder Alert ist deaktiviert.")).toBeInTheDocument();
    expect(screen.queryByText("Test-Alert in die Queue aufgenommen.")).not.toBeInTheDocument();
    expect(cardVisible(draft,"QuickServices",false)).toBe(true);
    expect(cardVisible({...draft,preferences:{...draft.preferences,showQuickServices:false}},"QuickServices",false)).toBe(false);
    expect(cardVisible(draft,"QuickServices",true)).toBe(false);
});

it("disconnects a connected OBS client and does not report an alert loading error as ready", async()=>{
    invoke.mockRejectedValueOnce(new Error("Alert-Datei beschädigt"));
    show("spotify","connected");
    expect(await screen.findByText("Alert-Datei beschädigt")).toBeInTheDocument();
    expect(screen.getByRole("button",{name:"Test-Alert"})).toBeDisabled();
    fireEvent.click(within(screen.getByRole("region",{name:"OBS Studio"})).getByRole("button",{name:"Trennen"}));
    await waitFor(()=>expect(invoke).toHaveBeenCalledWith("disconnect_obs",undefined));
    expect(invoke).not.toHaveBeenCalledWith("test_alert",expect.anything());
});

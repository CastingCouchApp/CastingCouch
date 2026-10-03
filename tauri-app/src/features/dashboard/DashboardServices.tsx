import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { FALLBACK_POLL_MS, queryKeys, tauriInvoke, type AlertDefinition, type ServiceStatus } from "../../lib/api";
import { musicProvider, type AppSettings } from "../../lib/app-settings";

const selectClass="rounded-md border border-border bg-input px-2 py-1 text-text";
function errorText(error:unknown) { return error instanceof Error ? error.message : String(error); }
type LaunchResult={status:"started"|"already_running";message:string};

function ServiceAction({label,disabled,run}: {label:string;disabled?:boolean;run:()=>Promise<unknown>}) {
    const client=useQueryClient();
    const [message,setMessage]=useState("");
    const action=useMutation({
        mutationFn:run,
        onMutate:()=>setMessage(""),
        onSuccess:(result)=>{
            if (result && typeof result === "object" && "message" in result && typeof result.message === "string") setMessage(result.message);
        },
        onSettled:()=>{
            void client.invalidateQueries({queryKey:queryKeys.services});
            void client.invalidateQueries({queryKey:queryKeys.musicPlayer});
            void client.invalidateQueries({queryKey:queryKeys.alertRuntime});
        },
    });
    return <div className="space-y-1">
        <Button disabled={disabled||action.isPending} onClick={()=>action.mutate()}>{action.isPending ? `${label} …` : label}</Button>
        {message && <p role="status" className="text-sm text-muted">{message}</p>}
        {action.error && <p role="alert" className="text-sm text-danger">{errorText(action.error)}</p>}
    </div>;
}
function ConnectAction({status,connect,disconnect}: {status?:ServiceStatus;connect:()=>Promise<unknown>;disconnect:()=>Promise<unknown>}) {
    return <ServiceAction label={status?.state === "connecting" ? "Verbinden …" : status?.state === "connected" ? "Trennen" : "Verbinden"} disabled={status?.state === "connecting"} run={status?.state === "connected" ? disconnect : connect}/>;
}

export function DashboardServices({settings,services}: {settings:AppSettings;services?:ServiceStatus[]}) {
    const provider=musicProvider(settings?.MusicPlayer);
    const obs=services?.find(s=>s.id==="obs");
    const twitch=services?.find(s=>s.id==="twitch");
    const music=services?.find(s=>s.id===provider);
    const alerts=useQuery({queryKey:queryKeys.alerts,queryFn:()=>tauriInvoke<AlertDefinition[]>("list_alerts"),refetchInterval:FALLBACK_POLL_MS});
    const enabledAlerts=(alerts.data ?? []).filter(a=>a.enabled);
    const [selected,setSelected]=useState("");
    const alertType=enabledAlerts.some(a=>a.type===selected) ? selected : enabledAlerts[0]?.type ?? "";
    return <Card className="space-y-3">
        <h2 className="text-lg font-medium">Dienste-Schnellzugriff</h2>
        <div className="grid gap-3 sm:grid-cols-2">
            <section aria-label="OBS Studio" className="space-y-2 rounded-lg border border-border p-3">
                <h3 className="font-medium">OBS Studio</h3>
                <div className="flex flex-wrap items-start gap-2">
                    <ConnectAction status={obs} connect={()=>tauriInvoke("connect_obs")} disconnect={()=>tauriInvoke("disconnect_obs")}/>
                    <ServiceAction label="Starten" run={()=>tauriInvoke<LaunchResult>("launch_service",{service:"obs"})}/>
                </div>
                <Link to="/services" className="text-sm text-primary">OBS öffnen</Link>
            </section>
            <section aria-label="Twitch" className="space-y-2 rounded-lg border border-border p-3">
                <h3 className="font-medium">Twitch-Kanal</h3>
                <ConnectAction status={twitch} connect={()=>tauriInvoke("twitch_login")} disconnect={()=>tauriInvoke("twitch_logout")}/>
                <Link to="/services" hash="twitch" className="text-sm text-primary">Twitch öffnen</Link>
            </section>
            <section key={provider} aria-label={provider==="spotify"?"Spotify":"YouTube Music"} className="space-y-2 rounded-lg border border-border p-3">
                <h3 className="font-medium">{provider==="spotify"?"Spotify-Player":"YouTube Music"}</h3>
                <div className="flex flex-wrap items-start gap-2">
                    <ConnectAction status={music} connect={()=>tauriInvoke("music_player_connect")} disconnect={()=>tauriInvoke("music_player_disconnect")}/>
                    {provider==="spotify" && <ServiceAction label="Starten" run={()=>tauriInvoke<LaunchResult>("launch_service",{service:"spotify"})}/>}
                </div>
                <Link to="/music" className="text-sm text-primary">Musikplayer öffnen</Link>
            </section>
            <section aria-label="Alerts" className="space-y-2 rounded-lg border border-border p-3">
                <h3 className="font-medium">Alerts</h3>
                <label className="block text-sm">Test-Alert-Typ
                    <select className={`${selectClass} block w-full`} disabled={alerts.isError||!alertType} value={alertType} onChange={e=>setSelected(e.target.value)}>
                        {!alertType && <option value="">Keine aktivierten Alerts</option>}
                        {enabledAlerts.map(alert=><option key={alert.type} value={alert.type}>{alert.type}</option>)}
                    </select>
                </label>
                {alerts.isError && <p role="alert" className="text-sm text-danger">{errorText(alerts.error)}</p>}
                <ServiceAction label="Test-Alert" disabled={alerts.isError||!alertType} run={async()=>{
                    const queued=await tauriInvoke<number>("test_alert",{alertType,user:"Test User"});
                    if (!queued) throw new Error("Kein Alert aufgenommen. Engine oder Alert ist deaktiviert.");
                    return {message:"Test-Alert in die Queue aufgenommen."};
                }}/>
                <Link to="/alerts" className="text-sm text-primary">Alerts öffnen</Link>
            </section>
        </div>
        <div className="flex flex-wrap gap-3 text-sm">
            <Link to="/overlay" className="text-primary">Overlay öffnen</Link>
            <Link to="/settings" className="text-primary">Programmpfade konfigurieren</Link>
        </div>
    </Card>;
}

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { useEffect, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { listenExtensionPacksChanged, tauriInvoke } from "../../lib/api";
type Asset = { id: string; name: string; url: string };
type Pack = {
    id: string;
    name: string;
    version: string;
    widgets?: unknown[];
    effects?: unknown[];
    animations?: unknown[];
    fonts?: unknown[];
    assets?: unknown[];
};
export function OverlayLibrary({ baseUrl }: { baseUrl: string }) {
    const client = useQueryClient();
    const [message, setMessage] = useState("");
    const native =
        typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
    const [eventError, setEventError] = useState<unknown>(null);
    useEffect(() => {
        if (!native) return;
        let disposed = false,
            unlisten: (() => void) | undefined;
        void listenExtensionPacksChanged(
            () =>
                void client.invalidateQueries({ queryKey: ["overlay-packs"] }),
        )
            .then((stop) => {
                if (disposed) stop();
                else unlisten = stop;
            })
            .catch((error) => {
                if (!disposed) setEventError(error);
            });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client, native]);
    async function request<T>(path: string, options?: RequestInit): Promise<T> {
        const response = await fetch(baseUrl + path, options);
        if (!response.ok) {
            let detail = await response.text();
            try {
                const parsed = JSON.parse(detail);
                if (typeof parsed.error === "string") detail = parsed.error;
            } catch {
                /* Plain-text server errors remain readable. */
            }
            throw new Error(detail || `HTTP ${response.status}`);
        }
        return response.json() as Promise<T>;
    }
    const assets = useQuery({
        queryKey: ["overlay-assets", baseUrl],
        queryFn: () => request<{ assets: Asset[] }>("/assets"),
    });
    const packs = useQuery({
        queryKey: ["overlay-packs", baseUrl],
        queryFn: async () => {
            const data = native
                ? { packs: await tauriInvoke<Pack[]>("list_extension_packs") }
                : await request<{ packs: Pack[] }>("/extensions");
            if (!Array.isArray(data?.packs))
                throw new Error("Ungültiger Extension-Pack-Katalog");
            return data;
        },
    });
    const update = useMutation({
        mutationFn: async ({
            path,
            file,
            filePath,
        }: {
            path: string;
            file?: File;
            filePath?: string;
        }) => {
            if (native && filePath) {
                const result = await tauriInvoke<Pack>(
                    "import_extension_pack",
                    { path: filePath },
                );
                return { path, imported: true, name: result.name };
            }
            if (
                native &&
                !file &&
                path !== "/extensions/install" &&
                path.startsWith("/extensions/")
            ) {
                await tauriInvoke("uninstall_extension_pack", {
                    id: decodeURIComponent(path.slice("/extensions/".length)),
                });
                return { path, imported: false, name: "" };
            }
            if (!file) {
                await request(path, { method: "DELETE" });
                return { path, imported: false, name: "" };
            }
            const body = new FormData();
            body.append("file", file);
            const result = await request<{ name?: string }>(path, {
                method: "POST",
                body,
            });
            return { path, imported: true, name: result.name || file.name };
        },
        onMutate: () => setMessage(""),
        onSuccess: (result) => {
            setMessage(
                result.path.startsWith("/extensions")
                    ? result.imported
                        ? `Extension Pack „${result.name}“ installiert.`
                        : "Extension Pack deinstalliert."
                    : result.imported
                      ? "Bild importiert."
                      : "Bild gelöscht.",
            );
            void client.invalidateQueries({ queryKey: ["overlay-assets"] });
            void client.invalidateQueries({ queryKey: ["overlay-packs"] });
        },
    });
    const choosePack = useMutation({
        mutationFn: () =>
            open({
                title: "Extension Pack importieren",
                multiple: false,
                directory: false,
                filters: [{ name: "Extension Pack", extensions: ["zip"] }],
            }),
        onSuccess: (path) => {
            if (typeof path === "string")
                update.mutate({ path: "/extensions/install", filePath: path });
        },
    });
    const busy = update.isPending || choosePack.isPending;
    return (
        <div className="grid gap-4 xl:grid-cols-2">
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Asset-Bibliothek</h2>
                <label className="block text-sm">
                    Bild importieren
                    <input
                        className="block mt-2"
                        type="file"
                        accept=".png,.jpg,.jpeg,.webp,.gif,.bmp,.svg"
                        disabled={update.isPending}
                        onChange={(e) => {
                            const file = e.target.files?.[0];
                            if (file) update.mutate({ path: "/assets", file });
                            e.target.value = "";
                        }}
                    />
                </label>
                <div className="grid grid-cols-2 gap-3">
                    {assets.data?.assets?.map((asset) => (
                        <div key={asset.id} className="space-y-2">
                            <img
                                className="h-24 w-full object-contain"
                                alt={asset.name}
                                src={baseUrl + asset.url}
                            />
                            <p className="truncate">{asset.name}</p>
                            <Button
                                onClick={() =>
                                    void navigator.clipboard.writeText(
                                        asset.url,
                                    )
                                }
                            >
                                URL kopieren
                            </Button>
                            <Button
                                variant="danger"
                                disabled={update.isPending}
                                onClick={() => {
                                    if (
                                        window.confirm(
                                            `Bild „${asset.name}“ löschen?`,
                                        )
                                    )
                                        update.mutate({
                                            path: `/assets/${encodeURIComponent(asset.id)}`,
                                        });
                                }}
                            >
                                Löschen
                            </Button>
                        </div>
                    ))}
                </div>
                {assets.error && <p role="alert">{String(assets.error)}</p>}
            </Card>
            <Card className="space-y-3">
                <div className="flex items-center justify-between gap-2">
                    <h2 className="text-lg font-semibold">Extension Packs</h2>
                    <Button
                        variant="ghost"
                        disabled={packs.isFetching || busy}
                        onClick={() => void packs.refetch()}
                    >
                        Packs aktualisieren
                    </Button>
                </div>
                <p className="text-sm text-text-secondary">
                    {packs.isPending
                        ? "Packs werden geladen …"
                        : !packs.data
                          ? "Pack-Katalog nicht geladen"
                          : `${packs.data?.packs.length ?? 0} Extension Packs installiert`}
                </p>
                {native ? (
                    <Button disabled={busy} onClick={() => choosePack.mutate()}>
                        ZIP importieren
                    </Button>
                ) : (
                    <label className="block text-sm">
                        ZIP importieren
                        <input
                            className="block mt-2"
                            type="file"
                            accept=".zip"
                            disabled={update.isPending}
                            onChange={(e) => {
                                const file = e.target.files?.[0];
                                if (file)
                                    update.mutate({
                                        path: "/extensions/install",
                                        file,
                                    });
                                e.target.value = "";
                            }}
                        />
                    </label>
                )}
                {packs.data?.packs?.map((pack) => (
                    <div key={pack.id} className="flex items-center gap-2">
                        <div className="flex-1">
                            <p>
                                {pack.name} · {pack.version}
                            </p>
                            <p className="text-sm text-text-secondary">
                                {pack.widgets?.length ?? 0} Widgets ·{" "}
                                {pack.effects?.length ?? 0} Effekte ·{" "}
                                {pack.animations?.length ?? 0} Animationen ·{" "}
                                {pack.fonts?.length ?? 0} Fonts ·{" "}
                                {pack.assets?.length ?? 0} Assets
                            </p>
                        </div>
                        <Button
                            variant="danger"
                            disabled={busy}
                            onClick={() => {
                                if (
                                    window.confirm(
                                        `Pack „${pack.name}“ deinstallieren?`,
                                    )
                                )
                                    update.mutate({
                                        path: `/extensions/${encodeURIComponent(pack.id)}`,
                                    });
                            }}
                        >
                            Deinstallieren
                        </Button>
                    </div>
                ))}
                <p className="text-sm text-text-secondary">
                    Nach Änderungen den Canvas-Editor neu öffnen, um die Palette
                    zu aktualisieren.
                </p>
                {packs.error && <p role="alert">{String(packs.error)}</p>}
                {choosePack.error && (
                    <p role="alert">{String(choosePack.error)}</p>
                )}
                {eventError != null && <p role="alert">{String(eventError)}</p>}
            </Card>
            {message && <p role="status">{message}</p>}
            {update.error && <p role="alert">{String(update.error)}</p>}
        </div>
    );
}

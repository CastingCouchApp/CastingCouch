import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
type Asset = { id: string; name: string; url: string };
type Pack = { id: string; name: string; version: string };
export function OverlayLibrary({ baseUrl }: { baseUrl: string }) {
    const client = useQueryClient();
    async function request<T>(path: string, options?: RequestInit): Promise<T> {
        const response = await fetch(baseUrl + path, options);
        if (!response.ok) throw new Error(await response.text());
        return response.json() as Promise<T>;
    }
    const assets = useQuery({
        queryKey: ["overlay-assets", baseUrl],
        queryFn: () => request<{ assets: Asset[] }>("/assets"),
    });
    const packs = useQuery({
        queryKey: ["overlay-packs", baseUrl],
        queryFn: () => request<{ packs: Pack[] }>("/extensions"),
    });
    const update = useMutation({
        mutationFn: ({ path, file }: { path: string; file?: File }) => {
            if (!file) return request(path, { method: "DELETE" });
            const body = new FormData();
            body.append("file", file);
            return request(path, { method: "POST", body });
        },
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["overlay-assets"] });
            void client.invalidateQueries({ queryKey: ["overlay-packs"] });
        },
    });
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
                <h2 className="text-lg font-semibold">Extension Packs</h2>
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
                {packs.data?.packs?.map((pack) => (
                    <div key={pack.id} className="flex items-center gap-2">
                        <span className="flex-1">
                            {pack.name} · {pack.version}
                        </span>
                        <Button
                            variant="danger"
                            disabled={update.isPending}
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
            </Card>
            {update.error && <p role="alert">{String(update.error)}</p>}
        </div>
    );
}

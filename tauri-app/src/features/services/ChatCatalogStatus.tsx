import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Button } from "../../components/ui/button";
type CatalogStatus = {
    channelId: string;
    emotes: number;
    badges: number;
    errors: string[];
    updatedAt: string | null;
};
export function ChatCatalogStatusPanel({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const queryKey = ["chat-catalog-status"];
    const status = useQuery({
        queryKey,
        queryFn: () => tauriInvoke<CatalogStatus>("chat_catalog_status"),
        refetchInterval: 5000,
    });
    const refresh = useMutation({
        mutationFn: () => tauriInvoke<CatalogStatus>("refresh_chat_catalogs"),
        onSuccess: (data) => client.setQueryData(queryKey, data),
    });
    return (
        <div className="space-y-2 text-sm">
            <div className="flex items-center justify-between gap-2">
                <span>
                    {status.data?.updatedAt
                        ? `${status.data.emotes} Emotes · ${status.data.badges} Badges`
                        : "Chat-Kataloge noch nicht geladen"}
                </span>
                <Button
                    variant="ghost"
                    disabled={!enabled || refresh.isPending}
                    onClick={() => refresh.mutate()}
                >
                    Chat-Kataloge aktualisieren
                </Button>
            </div>
            {status.data?.errors?.map((error, index) => (
                <p role="alert" key={index}>
                    {error}
                </p>
            ))}
            {status.error && <p role="alert">{String(status.error)}</p>}
            {refresh.error && <p role="alert">{String(refresh.error)}</p>}
        </div>
    );
}

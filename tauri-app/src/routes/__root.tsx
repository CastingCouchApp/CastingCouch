import { createRootRoute } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import { StartupGate } from "../components/layout/StartupGate";
import { AppShell } from "../components/layout/AppShell";

export const Route = createRootRoute({
    component: Root,
});

function Root() {
    return (
        <>
            <StartupGate>
                <AppShell />
            </StartupGate>
            {import.meta.env.DEV ? (
                <TanStackRouterDevtools position="bottom-right" />
            ) : null}
        </>
    );
}

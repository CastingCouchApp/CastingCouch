import { createFileRoute } from "@tanstack/react-router";
import { Card } from "../components/ui/card";

export const Route = createFileRoute("/about")({
  component: AboutPage,
});

function AboutPage() {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Über CastingCouch</h1>
      <Card>
        <p className="text-sm text-zinc-300">
          Tauri-Port der Creator Control Suite. Overlay-Editor und OBS-Browser-Sources nutzen weiterhin
          Loopback <code>http://127.0.0.1:8765</code>.
        </p>
      </Card>
    </div>
  );
}

import { createFileRoute } from "@tanstack/react-router";
import { Card } from "../components/ui/card";

export const Route = createFileRoute("/updates")({
  component: UpdatesPage,
});

function UpdatesPage() {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Updates</h1>
      <Card>
        <p className="text-sm text-zinc-300">
          Signierte GitHub-Releases (`update-manifest.json` + SHA-256) bleiben der Update-Kanal.
          Der Rust-Verifier prüft Manifest und ZIP analog zu LocalUpdateService.
        </p>
        <p className="mt-2 text-sm text-zinc-500">Kanal: Beta · Quelle: frankhildebrandt/CreatorControlSuite</p>
      </Card>
    </div>
  );
}

import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import {
  queryKeys,
  tauriInvoke,
  type AppVersionInfo,
  type UpdateCheckResult,
  type UpdatePackage,
} from "../lib/api";

export const Route = createFileRoute("/updates")({
  component: UpdatesPage,
});

const GITHUB_SOURCE = "CastingCouchApp/CastingCouch";

function UpdatesPage() {
  const [verified, setVerified] = useState(false);
  const [checksumError, setChecksumError] = useState<string | null>(null);

  const version = useQuery({
    queryKey: queryKeys.appVersion,
    queryFn: () => tauriInvoke<AppVersionInfo>("app_version"),
  });

  const check = useMutation({
    mutationFn: () => tauriInvoke<UpdateCheckResult>("check_updates"),
    onSuccess: () => {
      setVerified(false);
      setChecksumError(null);
    },
  });

  const download = useMutation({
    mutationFn: (pkg: UpdatePackage) => tauriInvoke<string>("download_update", { package: pkg }),
    onSuccess: () => {
      setVerified(true);
      setChecksumError(null);
    },
    onError: (error: unknown) => {
      setVerified(false);
      setChecksumError(String(error));
    },
  });

  const apply = useMutation({
    mutationFn: () => tauriInvoke<string>("apply_update"),
  });

  const result = check.data;
  const pkg = result?.package ?? null;
  const canDownload = Boolean(pkg);
  const canApply = verified && !checksumError;

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Updates</h1>

      <Card className="space-y-2">
        <p className="text-sm">
          Aktuelle Version: <strong>{version.data?.version ?? "…"}</strong>
        </p>
        <p className="text-sm text-muted">
          Kanal: {version.data?.channel ?? "…"} · Quelle: {GITHUB_SOURCE}
        </p>
        <p className="text-sm text-muted">
          Signierte GitHub-Releases (RSA + SHA-256). Tauri nutzt{" "}
          <code>update-manifest-tauri-win.json</code> /{" "}
          <code>update-manifest-tauri-macos.json</code>; WPF bleibt bei{" "}
          <code>update-manifest.json</code>. ProductId ist in beiden Stacks{" "}
          <code>CreatorControlSuite</code>.
        </p>
        <Button onClick={() => check.mutate()} disabled={check.isPending}>
          Prüfen
        </Button>
      </Card>

      {result ? (
        <Card className="space-y-2" data-testid="update-manifest">
          <h2 className="text-lg font-medium">Manifest</h2>
          <p className="text-sm">{result.detail}</p>
          {pkg ? (
            <dl className="grid gap-1 text-sm">
              <div>
                <dt className="text-muted">Version</dt>
                <dd>{pkg.version}</dd>
              </div>
              <div>
                <dt className="text-muted">SHA-256</dt>
                <dd className="break-all font-mono text-xs">{pkg.sha256}</dd>
              </div>
              <div>
                <dt className="text-muted">Paket</dt>
                <dd>{pkg.package_file_name}</dd>
              </div>
              <div>
                <dt className="text-muted">Release Notes</dt>
                <dd className="whitespace-pre-wrap">{pkg.release_notes || "—"}</dd>
              </div>
            </dl>
          ) : null}
        </Card>
      ) : null}

      {checksumError ? (
        <p className="text-sm text-danger" role="alert" data-testid="checksum-error">
          SHA-256-Prüfsumme ungültig ({checksumError})
        </p>
      ) : null}

      {apply.data ? <p className="text-sm text-success">{apply.data}</p> : null}
      {apply.error ? (
        <p className="text-sm text-danger" role="alert">
          {String(apply.error)}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <Button
          onClick={() => pkg && download.mutate(pkg)}
          disabled={!canDownload || download.isPending}
        >
          Download
        </Button>
        <Button onClick={() => apply.mutate()} disabled={!canApply || apply.isPending}>
          Installieren
        </Button>
      </div>
      <p className="text-sm text-muted">
        Vor der Installation wird der aktuelle App-Ordner nach Backups kopiert. Danach startet der
        NSIS-/MSI- bzw. DMG-Installer.
      </p>
    </div>
  );
}

import { createFileRoute } from "@tanstack/react-router";
import { useForm } from "@tanstack/react-form";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { Input } from "../components/ui/input";
import { queryClient, queryKeys, tauriInvoke, type AppSettings } from "../lib/api";

export const Route = createFileRoute("/settings")({
  component: SettingsPage,
});

const themes = [
  { id: "classic", label: "Classic" },
  { id: "neon-night-market", label: "Neon Night Market" },
  { id: "arctic-glass-lab", label: "Arctic Glass Lab" },
  { id: "pastel-lofi-cafe", label: "Pastel Lo-Fi Café" },
];

function SettingsPage() {
  const settings = useQuery({
    queryKey: queryKeys.settings,
    queryFn: () => tauriInvoke<AppSettings>("get_settings"),
  });
  const save = useMutation({
    mutationFn: (next: AppSettings) => tauriInvoke("save_settings", { settings: next }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.settings }),
  });

  if (!settings.data) {
    return <p className="text-zinc-400">Einstellungen werden geladen…</p>;
  }

  return <SettingsForm initial={settings.data} onSave={(s) => save.mutate(s)} saving={save.isPending} />;
}

function SettingsForm({
  initial,
  onSave,
  saving,
}: {
  initial: AppSettings;
  onSave: (s: AppSettings) => void;
  saving: boolean;
}) {
  const form = useForm({
    defaultValues: initial,
    onSubmit: ({ value }) => {
      document.documentElement.dataset.theme = value.General.ThemeId;
      onSave(value);
    },
  });

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        void form.handleSubmit();
      }}
    >
      <h1 className="text-2xl font-semibold">Einstellungen</h1>
      <Card className="space-y-3">
        <form.Field name="General.ThemeId">
          {(field) => (
            <label className="block text-sm">
              Theme
              <select
                className="mt-1 w-full rounded-md border border-white/15 bg-black/30 px-3 py-1.5"
                value={field.state.value}
                onChange={(e) => field.handleChange(e.target.value)}
              >
                {themes.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.label}
                  </option>
                ))}
              </select>
            </label>
          )}
        </form.Field>
        <form.Field name="Obs.Host">
          {(field) => (
            <label className="block text-sm">
              OBS Host
              <Input value={field.state.value} onChange={(e) => field.handleChange(e.target.value)} />
            </label>
          )}
        </form.Field>
        <form.Field name="Obs.Port">
          {(field) => (
            <label className="block text-sm">
              OBS Port
              <Input
                type="number"
                value={field.state.value}
                onChange={(e) => field.handleChange(Number(e.target.value))}
              />
            </label>
          )}
        </form.Field>
        <form.Field name="Twitch.ChannelName">
          {(field) => (
            <label className="block text-sm">
              Twitch-Kanal
              <Input value={field.state.value} onChange={(e) => field.handleChange(e.target.value)} />
            </label>
          )}
        </form.Field>
        <Button type="submit" disabled={saving}>
          Speichern
        </Button>
      </Card>
    </form>
  );
}

import { useEffect, useState } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { useForm } from "@tanstack/react-form";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { Input } from "../components/ui/input";
import {
  applyEditedSettings,
  applyThemeId,
  cloneSettings,
  THEME_CATALOG,
  type AppSettings,
} from "../lib/app-settings";
import { queryClient, queryKeys, tauriInvoke } from "../lib/api";

export const Route = createFileRoute("/settings")({
  component: SettingsPage,
});

const selectClass = "mt-1 w-full rounded-md border border-white/15 bg-black/30 px-3 py-1.5 text-sm";

function SettingsPage() {
  const settings = useQuery({
    queryKey: queryKeys.settings,
    queryFn: () => tauriInvoke<AppSettings>("get_settings"),
  });
  const hasPassword = useQuery({
    queryKey: ["obs-has-password"],
    queryFn: () => tauriInvoke<boolean>("obs_has_password"),
  });
  const save = useMutation({
    mutationFn: async ({ next, obsPassword }: { next: AppSettings; obsPassword: string }) => {
      await tauriInvoke("save_settings", { settings: next });
      if (obsPassword) {
        await tauriInvoke("set_obs_password", { password: obsPassword });
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.settings });
      queryClient.invalidateQueries({ queryKey: ["obs-has-password"] });
    },
  });

  useEffect(() => {
    if (settings.data?.General.ThemeId) {
      applyThemeId(settings.data.General.ThemeId);
    }
  }, [settings.data?.General.ThemeId]);

  if (!settings.data) {
    return <p className="text-zinc-400">Einstellungen werden geladen…</p>;
  }

  return (
    <SettingsForm
      initial={settings.data}
      obsHasPassword={hasPassword.data === true}
      onSave={(next, obsPassword) => save.mutate({ next, obsPassword })}
      saving={save.isPending}
    />
  );
}

function SettingsForm({
  initial,
  obsHasPassword,
  onSave,
  saving,
}: {
  initial: AppSettings;
  obsHasPassword: boolean;
  onSave: (settings: AppSettings, obsPassword: string) => void;
  saving: boolean;
}) {
  const [obsPassword, setObsPassword] = useState("");
  const form = useForm({
    defaultValues: cloneSettings(initial),
    onSubmit: ({ value }) => {
      const next = applyEditedSettings(initial, value);
      applyThemeId(next.General.ThemeId);
      onSave(next, obsPassword);
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
        <h2 className="text-lg font-medium">Allgemein</h2>
        <form.Field name="General.Language">
          {(field) => (
            <label className="block text-sm">
              Sprache
              <select
                className={selectClass}
                value={field.state.value}
                onChange={(e) => field.handleChange(e.target.value)}
              >
                <option value="de-DE">Deutsch</option>
                <option value="en-US">English</option>
              </select>
            </label>
          )}
        </form.Field>
        <form.Field name="General.ThemeId">
          {(field) => (
            <label className="block text-sm">
              Theme
              <select
                className={selectClass}
                value={field.state.value}
                onChange={(e) => {
                  field.handleChange(e.target.value);
                  applyThemeId(e.target.value);
                }}
              >
                {THEME_CATALOG.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.label}
                  </option>
                ))}
              </select>
            </label>
          )}
        </form.Field>
        <form.Field name="General.StartWithWindows">
          {(field) => (
            <Checkbox
              label="Mit Windows starten"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="General.MinimizeToTray">
          {(field) => (
            <Checkbox
              label="Beim Schließen in den Infobereich minimieren"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="General.TitleBarWidgetCardsEnabled">
          {(field) => (
            <Checkbox
              label="TitleBar-Widgets als Cards darstellen"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="General.ConnectionWatchdogEnabled">
          {(field) => (
            <Checkbox
              label="Verbindungen automatisch überwachen und bei Abbruch wiederherstellen"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="General.ConnectionWatchdogSeconds">
          {(field) => (
            <NumberField
              label="Prüfintervall in Sekunden"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <div className="flex flex-wrap gap-4">
          <form.Field name="General.ReconnectObs">
            {(field) => (
              <Checkbox label="OBS" checked={field.state.value} onChange={field.handleChange} />
            )}
          </form.Field>
          <form.Field name="General.ReconnectTwitch">
            {(field) => (
              <Checkbox label="Twitch" checked={field.state.value} onChange={field.handleChange} />
            )}
          </form.Field>
          <form.Field name="General.ReconnectSpotify">
            {(field) => (
              <Checkbox label="Spotify" checked={field.state.value} onChange={field.handleChange} />
            )}
          </form.Field>
          <form.Field name="General.ReconnectYouTubeMusic">
            {(field) => (
              <Checkbox
                label="YouTube Music"
                checked={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
          <form.Field name="General.ReconnectStreamerBot">
            {(field) => (
              <Checkbox
                label="Streamer.bot"
                checked={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
        </div>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-lg font-medium">OBS</h2>
        <form.Field name="Obs.Host">
          {(field) => (
            <TextField label="Host" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Obs.Port">
          {(field) => (
            <NumberField label="Port" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <label className="block text-sm">
          WebSocket-Passwort
          <Input
            type="password"
            autoComplete="off"
            placeholder={obsHasPassword ? "Passwort hinterlegt" : ""}
            value={obsPassword}
            onChange={(e) => setObsPassword(e.target.value)}
          />
        </label>
        <form.Field name="Obs.ExecutablePath">
          {(field) => (
            <TextField
              label="Programmpfad zu OBS"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Obs.AutoConnect">
          {(field) => (
            <Checkbox
              label="OBS automatisch verbinden"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Obs.ConnectOnPrepare">
          {(field) => (
            <Checkbox
              label="Bei Stream vorbereiten OBS verbinden"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <form.Field name="Obs.StartScene">
            {(field) => (
              <TextField
                label="Start-Szene"
                value={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
          <form.Field name="Obs.LiveScene">
            {(field) => (
              <TextField
                label="Live-Szene"
                value={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
          <form.Field name="Obs.PauseScene">
            {(field) => (
              <TextField
                label="Pause-Szene"
                value={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
          <form.Field name="Obs.EndScene">
            {(field) => (
              <TextField
                label="Ende-Szene"
                value={field.state.value}
                onChange={field.handleChange}
              />
            )}
          </form.Field>
        </div>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-lg font-medium">Twitch</h2>
        <form.Field name="Twitch.ClientId">
          {(field) => (
            <TextField label="Client-ID" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Twitch.ChannelName">
          {(field) => (
            <TextField label="Kanalname" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Twitch.CreatorDashboardUrl">
          {(field) => (
            <TextField
              label="Creator-Dashboard-URL"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Twitch.AutoConnect">
          {(field) => (
            <Checkbox
              label="Twitch automatisch verbinden"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Twitch.ConnectOnPrepare">
          {(field) => (
            <Checkbox
              label="Bei Stream vorbereiten Twitch verbinden"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Twitch.EnableChat">
          {(field) => (
            <Checkbox
              label="Live-Chat aktivieren"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Twitch.EnableEventSub">
          {(field) => (
            <Checkbox
              label="Follower, Subs, Raids und Cheers empfangen"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-lg font-medium">Spotify</h2>
        <form.Field name="Spotify.ClientId">
          {(field) => (
            <TextField label="Client-ID" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Spotify.RedirectUri">
          {(field) => (
            <TextField
              label="Redirect-URI"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Spotify.AutoConnect">
          {(field) => (
            <Checkbox
              label="Spotify automatisch verbinden"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-lg font-medium">Overlay</h2>
        <form.Field name="Overlay.WebServerPort">
          {(field) => (
            <NumberField label="Port" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Overlay.SelectedCanvasId">
          {(field) => (
            <label className="block text-sm">
              Ausgewähltes Canvas
              <select
                className={selectClass}
                value={field.state.value}
                onChange={(e) => field.handleChange(e.target.value)}
              >
                {initial.Overlay.Canvases.map((canvas) => (
                  <option key={canvas.Id} value={canvas.Id}>
                    {canvas.Name}
                  </option>
                ))}
              </select>
            </label>
          )}
        </form.Field>
        <form.Field name="Overlay.Chat.Enabled">
          {(field) => (
            <Checkbox
              label="Chat-Overlay aktiv"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Overlay.Chat.ShowTwitchEvents">
          {(field) => (
            <Checkbox
              label="Twitch-Events einblenden (Follow, Sub, Raid, …)"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Overlay.Chat.EnableBttv">
          {(field) => (
            <Checkbox
              label="BTTV-Emotes"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Overlay.Chat.EnableFfz">
          {(field) => (
            <Checkbox
              label="FrankerFaceZ-Emotes"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Overlay.Chat.EnableSevenTv">
          {(field) => (
            <Checkbox
              label="7TV-Emotes"
              checked={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-lg font-medium">Branding</h2>
        <form.Field name="Branding.DisplayName">
          {(field) => (
            <TextField
              label="Anzeigename"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Branding.ChannelName">
          {(field) => (
            <TextField label="Kanalname" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
        <form.Field name="Branding.AccentColor">
          {(field) => (
            <TextField
              label="Akzentfarbe"
              value={field.state.value}
              onChange={field.handleChange}
            />
          )}
        </form.Field>
        <form.Field name="Branding.LogoPath">
          {(field) => (
            <TextField label="Logo-Pfad" value={field.state.value} onChange={field.handleChange} />
          )}
        </form.Field>
      </Card>

      <Button type="submit" disabled={saving}>
        Speichern
      </Button>
    </form>
  );
}

function TextField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block text-sm">
      {label}
      <Input value={value} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}

function NumberField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
}) {
  return (
    <label className="block text-sm">
      {label}
      <Input
        type="number"
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </label>
  );
}

function Checkbox({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex items-center gap-2 text-sm">
      <input
        type="checkbox"
        className="size-4 accent-[var(--color-brand)]"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      {label}
    </label>
  );
}
